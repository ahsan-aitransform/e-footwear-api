-- Down of 0400: removes the inventory posting functions and the three extra tables, and restores the baseline inv.decide_loss_case
-- (including its integer-versus-uuid defect, so a rollback leaves the database exactly as the baseline had it).
-- Stock documents already posted by the removed functions stay in inv.documents / inv.stock_ledger; the notes in inv.document_extras (kind of an
-- adjustment, link of a void to its original, audit summary), the audit scans and the client ids of claim requests are lost.
DROP FUNCTION inv.cancel_transfer(uuid, uuid, text);
DROP FUNCTION inv.record_transfer_scan(uuid, uuid, uuid, text, text, integer, uuid);
DROP FUNCTION inv.create_transfer(uuid, uuid, uuid, uuid, text);
DROP FUNCTION inv.request_claim(uuid, uuid, date, uuid, uuid, numeric, text, text);
DROP FUNCTION inv.cancel_stock_audit(uuid, uuid);
DROP FUNCTION inv.post_stock_audit(uuid, uuid, boolean);
DROP FUNCTION inv.record_audit_scan(uuid, uuid, text, integer, uuid);
DROP FUNCTION inv.create_stock_audit(uuid, uuid, date, uuid, text);
DROP FUNCTION inv.void_stock_adjustment(uuid, uuid, text, uuid);
DROP FUNCTION inv.post_stock_adjustment(uuid, uuid, date, uuid, text, jsonb, text);
DROP FUNCTION inv.apply_document_lines(uuid, uuid, uuid, date, text, jsonb);
DROP FUNCTION inv.post_stock_variance_journal(uuid, uuid, date, text, uuid, text, numeric);
DROP FUNCTION inv.stock_unit_cost(uuid, uuid, uuid);
DROP FUNCTION inv.ensure_permission(uuid, text, text);

DROP TABLE inv.client_transactions;
DROP TABLE inv.stock_audit_scans;
DROP TABLE inv.document_extras;

CREATE OR REPLACE FUNCTION inv.decide_loss_case(p_company uuid, p_case uuid, p_decision text,
                                                p_bearer text, p_user uuid)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    c          inv.transfer_loss_cases%ROWTYPE;
    t          inv.transfers%ROWTYPE;
    l          inv.transfer_lines%ROWTYPE;
    v_date     date;
    v_transit  uuid;
    v_target   uuid;
    v_store    uuid;
    v_entry    uuid;
    v_value    numeric;
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'TRANSFER_LOSS_DECIDE');
    PERFORM set_config('app.internal_transfer_write', 'on', true);
    SELECT * INTO c FROM inv.transfer_loss_cases WHERE company_id = p_company AND id = p_case FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'loss case % not found', p_case; END IF;
    IF c.status <> 'OPEN' THEN
        RAISE EXCEPTION 'loss case % is %, only an OPEN case can be decided', c.id, c.status USING ERRCODE = 'check_violation';
    END IF;
    SELECT * INTO t FROM inv.transfers WHERE company_id = p_company AND id = c.transfer_id;
    SELECT * INTO l FROM inv.transfer_lines WHERE company_id = p_company AND id = c.transfer_line_id;
    SELECT (now() AT TIME ZONE tz.timezone)::date INTO v_date FROM platform.companies tz WHERE tz.id = p_company;
    SELECT id INTO v_transit FROM acct.accounts WHERE company_id = p_company AND store_id = '00000000-0000-0000-0000-000000000000'::uuid AND system_role = 'TRANSIT';
    v_value := core.round_money(p_company, c.qty * l.unit_cost);

    IF p_decision = 'FOUND' THEN
        PERFORM inv.post_stock_move(p_company, t.to_store_id, l.variant_id, c.qty, 'TRANSFER_FOUND', c.id, l.unit_cost, v_date);
        SELECT id INTO v_target FROM acct.accounts WHERE company_id = p_company AND store_id = t.to_store_id AND system_role = 'STOCK';
        v_store := t.to_store_id;
        UPDATE inv.transfer_loss_cases SET status = 'FOUND', decided_by = p_user, decided_at = now()
         WHERE company_id = p_company AND id = p_case;
        INSERT INTO inv.transfer_events (company_id, transfer_id, variant_id, event_type, qty, user_id)
        VALUES (p_company, t.id, l.variant_id, 'MISSING_FOUND', c.qty, p_user);
    ELSIF p_decision = 'CHARGE' THEN
        IF p_bearer NOT IN ('COMPANY','SENDER_STORE','RECEIVER_STORE') THEN
            RAISE EXCEPTION 'bearer % is not valid', p_bearer USING ERRCODE = 'check_violation';
        END IF;
        v_store := CASE p_bearer WHEN 'COMPANY' THEN 0 WHEN 'SENDER_STORE' THEN t.from_store_id ELSE t.to_store_id END;
        SELECT id INTO v_target FROM acct.accounts WHERE company_id = p_company AND store_id = v_store AND system_role = 'STOCK_LOSS';
        UPDATE inv.transfer_loss_cases SET status = 'WRITTEN_OFF', bearer = p_bearer, decided_by = p_user, decided_at = now()
         WHERE company_id = p_company AND id = p_case;
        INSERT INTO inv.transfer_events (company_id, transfer_id, variant_id, event_type, qty, reason, user_id)
        VALUES (p_company, t.id, l.variant_id, 'LOSS_WRITTEN_OFF', c.qty, p_bearer, p_user);
    ELSE
        RAISE EXCEPTION 'decision % is not valid (use FOUND or CHARGE)', p_decision USING ERRCODE = 'check_violation';
    END IF;

    IF v_value > 0 THEN
        INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
        VALUES (p_company, v_date, v_store, 'LOSS', c.id, 'Transfer ' || t.transfer_no || ' missing pieces ' || p_decision, p_user)
        RETURNING id INTO v_entry;
        INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, debit, credit)
        VALUES (p_company, v_entry, v_date, 1, v_store, v_target, v_value, 0),
               (p_company, v_entry, v_date, 2, '00000000-0000-0000-0000-000000000000'::uuid, v_transit, 0, v_value);
        UPDATE inv.transfer_loss_cases SET journal_entry_id = v_entry
         WHERE company_id = p_company AND id = p_case;
    END IF;
END $$;

-- Restores the baseline check on approved claims. This fails when a claim was approved at zero cost (without a journal entry) after the up migration;
-- those rows have to be looked at by a person before the check can come back.
ALTER TABLE inv.claims DROP CONSTRAINT claims_approved_has_decider_and_journal;
ALTER TABLE inv.claims ADD CONSTRAINT claims_check CHECK (status <> 'APPROVED' OR (decided_by IS NOT NULL AND journal_entry_id IS NOT NULL));

-- Restores the baseline store-level policies of the journal tables (a head office line can then be written only by a person with all stores).
DROP POLICY store_isolation ON acct.journal_lines;
CREATE POLICY store_isolation ON acct.journal_lines AS RESTRICTIVE
    USING (core.store_allowed(store_id)) WITH CHECK (core.store_allowed(store_id));
DROP POLICY store_isolation ON acct.account_month_balances;
CREATE POLICY store_isolation ON acct.account_month_balances AS RESTRICTIVE
    USING (core.store_allowed(store_id)) WITH CHECK (core.store_allowed(store_id));
