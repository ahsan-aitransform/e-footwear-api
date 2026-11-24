-- 0400 Inventory: stock adjustments, stock audits, product claims, scan-based transfers, reconciliation
-- (Phase 0 rows ORG-4.16, ORG-4.17, ORG-5.7 to ORG-5.12, ORG-8.20 to ORG-8.24, ORG-8.31, ADD-10-01..04, ADD-19-01..05;
--  PROJECT_REQUIREMENTS Part 10 and Part 19).
--
-- What the baseline already does (not repeated here): inv.post_stock_move keeps stock_balances and the append-only stock_ledger in step,
-- inv.ship_transfer / inv.close_transfer / inv.decide_loss_case move and account for transfers, inv.approve_claim / inv.reject_claim decide claims,
-- inv.transfer_scans carries every scanned piece, and the triggers inv.check_transfer_scan / inv.apply_transfer_scan validate and apply the scans.
--
-- What this migration adds:
--   1. inv.document_extras: the kind of an adjustment (typed, count update, reversal), the link of a reversal to the adjustment it cancels, and the summary of
--      a posted audit. The baseline has no column for them. Append-only.
--   2. inv.stock_audit_scans: the count of a stock audit while it is being counted. inv.document_lines cannot hold it (a line needs a non-zero difference).
--      Append-only; a correction is a negative scan, never an edit.
--   3. inv.client_transactions: the client transaction id of requests whose table has no column for it (a claim request), so a retry creates one claim.
--   4. inv.post_stock_adjustment / inv.void_stock_adjustment: one transaction = document + lines + stock movements + journal. A void is a NEW reversing document.
--   5. inv.create_stock_audit / record_audit_scan / post_stock_audit / cancel_stock_audit: scan-based counting, then "Adjust Stock" posts the differences.
--   6. inv.request_claim: a claim request with its number (approval and rejection stay with the baseline functions).
--   7. inv.create_transfer / record_transfer_scan / cancel_transfer: the sending and receiving scans are idempotent (one client id per scan call).
--   8. BASELINE FIX: inv.decide_loss_case compared the integer 0 with a uuid inside a CASE (a leftover of the old numeric ids), so CHARGE failed at run time.
--      The function is replaced with the same signature and the same behaviour; only that expression is corrected. FOUND is unchanged.
--
-- Accounting treatment of adjustments and audit differences is NOT defined by the business ([BUSINESS DECISION REQUIRED]). Until it is, the net value of a
-- document is booked with the accounts the baseline already uses for stock losses (claims, lost pieces): a net decrease is Dr STOCK LOSS / Cr STOCK of the store,
-- a net increase is the reverse. Changing the treatment later means changing only inv.post_stock_variance_journal.
--
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

-- ---------------------------------------------------------------------
-- 1. Tables
-- ---------------------------------------------------------------------
CREATE TABLE inv.document_extras (
    company_id               uuid NOT NULL REFERENCES platform.companies(id),
    document_id              uuid NOT NULL,
    store_id                 uuid NOT NULL,
    kind                     text NOT NULL CHECK (kind IN ('MANUAL', 'COUNT_UPDATE', 'REVERSAL', 'AUDIT_ADJUSTMENT')),
    reverses_document_id     uuid,
    counted_item_count       integer CHECK (counted_item_count IS NULL OR counted_item_count >= 0),
    matched_item_count       integer CHECK (matched_item_count IS NULL OR matched_item_count >= 0),
    includes_unscanned_items boolean,
    created_at               timestamptz NOT NULL DEFAULT now(),
    created_by               uuid DEFAULT core.current_user_id(),
    PRIMARY KEY (company_id, document_id),
    FOREIGN KEY (company_id, document_id)         REFERENCES inv.documents(company_id, id),
    FOREIGN KEY (company_id, reverses_document_id) REFERENCES inv.documents(company_id, id),
    FOREIGN KEY (company_id, store_id)            REFERENCES core.stores(company_id, id)
);
CREATE UNIQUE INDEX ux_document_extras_reversal ON inv.document_extras (company_id, reverses_document_id) WHERE reverses_document_id IS NOT NULL;

CREATE TABLE inv.stock_audit_scans (
    company_id        uuid NOT NULL REFERENCES platform.companies(id),
    id                uuid NOT NULL DEFAULT uuidv7(),
    document_id       uuid NOT NULL,
    store_id          uuid NOT NULL,
    barcode           text NOT NULL,
    variant_id        uuid NOT NULL,
    quantity          integer NOT NULL CHECK (quantity <> 0 AND quantity BETWEEN -1000 AND 1000),
    device_scan_uuid  uuid NOT NULL,
    scanned_by        uuid DEFAULT core.current_user_id(),
    scanned_at        timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, device_scan_uuid),
    FOREIGN KEY (company_id, document_id) REFERENCES inv.documents(company_id, id),
    FOREIGN KEY (company_id, store_id)    REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, variant_id)  REFERENCES catalog.variants(company_id, id)
);
CREATE INDEX ix_stock_audit_scans_document ON inv.stock_audit_scans (company_id, document_id, variant_id);

CREATE TABLE inv.client_transactions (
    company_id            uuid NOT NULL REFERENCES platform.companies(id),
    client_transaction_id uuid NOT NULL,
    entity_type           text NOT NULL,
    entity_id             uuid NOT NULL,
    created_at            timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, client_transaction_id)
);

CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON inv.document_extras
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON inv.stock_audit_scans
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON inv.client_transactions
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();

SELECT core.enable_rls('inv.document_extras'::regclass, true);
SELECT core.enable_rls('inv.stock_audit_scans'::regclass, true);
SELECT core.enable_rls('inv.client_transactions'::regclass, false);
GRANT SELECT, INSERT ON inv.document_extras, inv.stock_audit_scans, inv.client_transactions TO pos_app;

-- ---------------------------------------------------------------------
-- 2. Helpers
-- ---------------------------------------------------------------------
-- A permission check with a sentence a person can read (sec.require_permission raises a bare "user <id> lacks permission <code>").
CREATE FUNCTION inv.ensure_permission(p_company uuid, p_code text, p_message text) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), p_code);
EXCEPTION WHEN insufficient_privilege THEN
    RAISE EXCEPTION '%', p_message USING ERRCODE = 'insufficient_privilege';
END $$;

-- The cost an INCREASE of stock is booked at when the person gives none: the average cost already held, else the purchase price of the size, else 0.
-- A technical default that keeps the average cost of the store from being diluted (a suggestion, not a business rule).
CREATE FUNCTION inv.stock_unit_cost(p_company uuid, p_store uuid, p_variant uuid) RETURNS numeric
LANGUAGE sql STABLE AS $$
    SELECT COALESCE(
        (SELECT NULLIF(b.avg_cost, 0) FROM inv.stock_balances b
          WHERE b.company_id = p_company AND b.store_id = p_store AND b.variant_id = p_variant),
        (SELECT NULLIF(vp.purchase_price, 0) FROM catalog.variant_prices vp
          WHERE vp.company_id = p_company AND vp.variant_id = p_variant),
        0)
$$;

-- Books the net value of a stock document. See the header: the accounting treatment is an open business decision.
CREATE FUNCTION inv.post_stock_variance_journal(p_company uuid, p_store uuid, p_date date, p_source_type text, p_source_id uuid,
                                                p_description text, p_net_value numeric) RETURNS uuid
LANGUAGE plpgsql AS $$
DECLARE
    v_value  core.money := core.round_money(p_company, abs(p_net_value));
    v_stock  uuid;
    v_loss   uuid;
    v_entry  uuid;
    v_no     int := 1;
BEGIN
    IF v_value = 0 THEN
        RETURN NULL;
    END IF;
    SELECT id INTO v_stock FROM acct.accounts WHERE company_id = p_company AND store_id = p_store AND system_role = 'STOCK';
    SELECT id INTO v_loss  FROM acct.accounts WHERE company_id = p_company AND store_id = p_store AND system_role = 'STOCK_LOSS';
    IF v_stock IS NULL OR v_loss IS NULL THEN
        RAISE EXCEPTION 'the stock accounts of this store are missing' USING ERRCODE = 'check_violation';
    END IF;
    INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
    VALUES (p_company, p_date, p_store, p_source_type, p_source_id, p_description, core.current_user_id())
    RETURNING id INTO v_entry;
    IF p_net_value > 0 THEN
        v_no := acct.add_line(p_company, v_entry, p_date, v_no, p_store, v_stock, NULL, v_value, 0, p_description);
        v_no := acct.add_line(p_company, v_entry, p_date, v_no, p_store, v_loss,  NULL, 0, v_value, p_description);
    ELSE
        v_no := acct.add_line(p_company, v_entry, p_date, v_no, p_store, v_loss,  NULL, v_value, 0, p_description);
        v_no := acct.add_line(p_company, v_entry, p_date, v_no, p_store, v_stock, NULL, 0, v_value, p_description);
    END IF;
    RETURN v_entry;
END $$;

-- Posts the lines of an adjustment or audit document: locks the sizes in ascending order (deadlock-free), writes inv.document_lines and moves the stock
-- through inv.post_stock_move. p_lines: [{"variantId": "...", "quantity": -2, "unitCost": 500, "reason": "...", "expectedQuantity": 20, "countedQuantity": 18}]
-- quantity is the signed movement; expectedQuantity (when given) must still be the stock on hand, otherwise nothing is posted. Returns the net value.
CREATE FUNCTION inv.apply_document_lines(p_company uuid, p_store uuid, p_document uuid, p_date date, p_doc_type text, p_lines jsonb) RETURNS numeric
LANGUAGE plpgsql AS $$
DECLARE
    l          record;
    v_on_hand  numeric;
    v_cost     numeric;
    v_applied  numeric;
    v_value    numeric := 0;
    v_no       int := 0;
    v_name     text;
BEGIN
    FOR l IN
        SELECT (e->>'variantId')::uuid                    AS variant_id,
               (e->>'quantity')::numeric                  AS qty,
               NULLIF(e->>'unitCost', '')::numeric        AS unit_cost,
               NULLIF(btrim(e->>'reason'), '')            AS reason,
               NULLIF(e->>'expectedQuantity', '')::numeric AS expected_qty,
               NULLIF(e->>'countedQuantity', '')::numeric AS counted_qty
          FROM jsonb_array_elements(p_lines) e
         ORDER BY (e->>'variantId')::uuid
    LOOP
        IF l.qty IS NULL OR l.qty = 0 THEN
            RAISE EXCEPTION 'every line needs a quantity other than zero' USING ERRCODE = 'check_violation';
        END IF;
        SELECT p.name || ' (size ' || v.size_code || ', barcode ' || v.barcode || ')' INTO v_name
          FROM catalog.variants v JOIN catalog.products p ON p.company_id = v.company_id AND p.id = v.product_id
         WHERE v.company_id = p_company AND v.id = l.variant_id AND NOT v.is_deleted;
        IF v_name IS NULL THEN
            RAISE EXCEPTION 'one of the sizes was not found or has been deleted' USING ERRCODE = 'check_violation';
        END IF;

        INSERT INTO inv.stock_balances (company_id, store_id, variant_id) VALUES (p_company, p_store, l.variant_id)
        ON CONFLICT (company_id, store_id, variant_id) DO NOTHING;
        SELECT b.qty_on_hand INTO v_on_hand FROM inv.stock_balances b
         WHERE b.company_id = p_company AND b.store_id = p_store AND b.variant_id = l.variant_id FOR UPDATE;

        IF l.expected_qty IS NOT NULL AND l.expected_qty <> v_on_hand THEN
            RAISE EXCEPTION 'the stock of % changed from % to % since the list was loaded: load it again', v_name, l.expected_qty, v_on_hand
                USING ERRCODE = 'check_violation';
        END IF;
        IF l.qty < 0 AND v_on_hand + l.qty < 0 THEN
            RAISE EXCEPTION 'cannot remove % pieces of %: only % in stock', -l.qty, v_name, v_on_hand USING ERRCODE = 'check_violation';
        END IF;

        v_cost := CASE WHEN l.qty > 0 THEN COALESCE(l.unit_cost, inv.stock_unit_cost(p_company, p_store, l.variant_id)) ELSE NULL END;
        v_applied := inv.post_stock_move(p_company, p_store, l.variant_id, l.qty, p_doc_type, p_document, v_cost, p_date);
        v_no := v_no + 1;
        -- counted is always expected + movement, so the table check (counted - expected = qty) holds whatever the caller sent
        INSERT INTO inv.document_lines (company_id, document_id, line_no, variant_id, expected_qty, counted_qty, qty, unit_cost, reason)
        VALUES (p_company, p_document, v_no, l.variant_id, l.expected_qty,
                CASE WHEN l.expected_qty IS NOT NULL THEN l.expected_qty + l.qty ELSE NULL END, l.qty, v_applied, l.reason);
        v_value := v_value + l.qty * v_applied;
    END LOOP;
    RETURN v_value;
END $$;

-- ---------------------------------------------------------------------
-- 3. Stock adjustment (docx 5.7) and count update of the non-zero stock list (docx 5.8)
--    p_kind MANUAL: typed adjustment; COUNT_UPDATE: the lines carry expectedQuantity and countedQuantity.
-- ---------------------------------------------------------------------
CREATE FUNCTION inv.post_stock_adjustment(p_company uuid, p_store uuid, p_date date, p_client_transaction uuid, p_description text,
                                          p_lines jsonb, p_kind text DEFAULT 'MANUAL') RETURNS uuid
LANGUAGE plpgsql AS $$
DECLARE
    c_head_office constant uuid := '00000000-0000-0000-0000-000000000000';
    v_existing  uuid;
    v_document  uuid := uuidv7();
    v_doc_no    bigint;
    v_today     date;
    v_count     int;
    v_value     numeric;
BEGIN
    PERFORM inv.ensure_permission(p_company, 'STOCK_ADJUST', 'You are not allowed to adjust stock.');
    IF p_client_transaction IS NULL THEN
        RAISE EXCEPTION 'a client transaction id is required' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('inv.client_transaction:' || p_client_transaction::text, 0));
    SELECT id INTO v_existing FROM inv.documents WHERE company_id = p_company AND document_uuid = p_client_transaction;
    IF v_existing IS NOT NULL THEN
        RETURN v_existing;
    END IF;

    IF p_kind NOT IN ('MANUAL', 'COUNT_UPDATE') THEN
        RAISE EXCEPTION 'unknown adjustment kind %', p_kind USING ERRCODE = 'check_violation';
    END IF;
    IF p_store IS NULL OR p_store = c_head_office THEN
        RAISE EXCEPTION 'choose the store whose stock is adjusted' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM 1 FROM core.stores WHERE company_id = p_company AND id = p_store AND is_active;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'the store was not found or is not active' USING ERRCODE = 'check_violation';
    END IF;
    SELECT (now() AT TIME ZONE c.timezone)::date INTO v_today FROM platform.companies c WHERE c.id = p_company;
    IF p_date IS NULL OR p_date > v_today THEN
        RAISE EXCEPTION 'the adjustment date cannot be in the future' USING ERRCODE = 'check_violation';
    END IF;
    IF p_lines IS NULL OR jsonb_typeof(p_lines) <> 'array' OR jsonb_array_length(p_lines) < 1 THEN
        RAISE EXCEPTION 'add at least one item to the adjustment' USING ERRCODE = 'check_violation';
    END IF;
    v_count := jsonb_array_length(p_lines);
    IF v_count > 500 THEN
        RAISE EXCEPTION 'an adjustment can have at most 500 lines' USING ERRCODE = 'check_violation';
    END IF;
    IF (SELECT count(DISTINCT e->>'variantId') FROM jsonb_array_elements(p_lines) e) <> v_count THEN
        RAISE EXCEPTION 'each size can appear only once in an adjustment' USING ERRCODE = 'check_violation';
    END IF;

    v_doc_no := core.next_number(p_company, p_store, 'STOCK_ADJUSTMENT');
    INSERT INTO inv.documents (company_id, id, store_id, doc_type, doc_no, doc_date, status, reason, document_uuid, posted_at, posted_by, created_by)
    VALUES (p_company, v_document, p_store, 'ADJUSTMENT', v_doc_no, p_date, 'POSTED', NULLIF(btrim(p_description), ''), p_client_transaction,
            now(), core.current_user_id(), core.current_user_id());
    INSERT INTO inv.document_extras (company_id, document_id, store_id, kind) VALUES (p_company, v_document, p_store, p_kind);

    v_value := inv.apply_document_lines(p_company, p_store, v_document, p_date, 'ADJUSTMENT', p_lines);
    PERFORM inv.post_stock_variance_journal(p_company, p_store, p_date, 'STOCK_ADJUSTMENT', v_document,
                                            'Stock adjustment ' || v_doc_no, v_value);
    RETURN v_document;
END $$;

-- A void is a NEW document that undoes the lines of the original at their own cost; the original is marked CANCELLED and stays in the history.
-- Undoing an increase needs the pieces to still be in stock (nothing is posted otherwise).
CREATE FUNCTION inv.void_stock_adjustment(p_company uuid, p_document uuid, p_reason text, p_client_transaction uuid) RETURNS uuid
LANGUAGE plpgsql AS $$
DECLARE
    o            inv.documents%ROWTYPE;
    v_existing   uuid;
    v_reversal   uuid := uuidv7();
    v_doc_no     bigint;
    v_today      date;
    v_lines      jsonb;
    v_value      numeric;
BEGIN
    PERFORM inv.ensure_permission(p_company, 'STOCK_ADJUST', 'You are not allowed to adjust stock.');
    IF p_client_transaction IS NULL THEN
        RAISE EXCEPTION 'a client transaction id is required' USING ERRCODE = 'check_violation';
    END IF;
    IF btrim(COALESCE(p_reason, '')) = '' THEN
        RAISE EXCEPTION 'give the reason for voiding the adjustment' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('inv.client_transaction:' || p_client_transaction::text, 0));
    SELECT id INTO v_existing FROM inv.documents WHERE company_id = p_company AND document_uuid = p_client_transaction;
    IF v_existing IS NOT NULL THEN
        RETURN v_existing;
    END IF;

    SELECT * INTO o FROM inv.documents WHERE company_id = p_company AND id = p_document AND NOT is_deleted FOR UPDATE;
    IF NOT FOUND OR o.doc_type <> 'ADJUSTMENT' THEN
        RAISE EXCEPTION 'the adjustment was not found' USING ERRCODE = 'no_data_found';
    END IF;
    IF o.status <> 'POSTED' THEN
        RAISE EXCEPTION 'adjustment % is %, only a posted adjustment can be voided', o.doc_no, o.status USING ERRCODE = 'check_violation';
    END IF;

    SELECT (now() AT TIME ZONE c.timezone)::date INTO v_today FROM platform.companies c WHERE c.id = p_company;
    SELECT jsonb_agg(jsonb_build_object('variantId', l.variant_id, 'quantity', -l.qty, 'unitCost', l.unit_cost,
                                        'reason', 'Void of adjustment ' || o.doc_no) ORDER BY l.line_no)
      INTO v_lines FROM inv.document_lines l WHERE l.company_id = p_company AND l.document_id = o.id;
    IF v_lines IS NULL THEN
        RAISE EXCEPTION 'adjustment % has no lines to void', o.doc_no USING ERRCODE = 'check_violation';
    END IF;

    v_doc_no := core.next_number(p_company, o.store_id, 'STOCK_ADJUSTMENT');
    INSERT INTO inv.documents (company_id, id, store_id, doc_type, doc_no, doc_date, status, reason, document_uuid, posted_at, posted_by, created_by)
    VALUES (p_company, v_reversal, o.store_id, 'ADJUSTMENT', v_doc_no, v_today, 'POSTED',
            'Void of adjustment ' || o.doc_no || ': ' || btrim(p_reason), p_client_transaction, now(), core.current_user_id(), core.current_user_id());
    INSERT INTO inv.document_extras (company_id, document_id, store_id, kind, reverses_document_id)
    VALUES (p_company, v_reversal, o.store_id, 'REVERSAL', o.id);

    v_value := inv.apply_document_lines(p_company, o.store_id, v_reversal, v_today, 'ADJUSTMENT', v_lines);
    PERFORM inv.post_stock_variance_journal(p_company, o.store_id, v_today, 'STOCK_ADJUSTMENT', v_reversal,
                                            'Void of stock adjustment ' || o.doc_no, v_value);
    UPDATE inv.documents SET status = 'CANCELLED' WHERE company_id = p_company AND id = o.id;
    RETURN v_reversal;
END $$;

-- ---------------------------------------------------------------------
-- 4. Stock audit (docx 5.12 and 8.24): count by scanning, compare with the books, then "Adjust Stock"
-- ---------------------------------------------------------------------
CREATE FUNCTION inv.create_stock_audit(p_company uuid, p_store uuid, p_date date, p_client_transaction uuid, p_note text) RETURNS uuid
LANGUAGE plpgsql AS $$
DECLARE
    c_head_office constant uuid := '00000000-0000-0000-0000-000000000000';
    v_existing uuid;
    v_document uuid := uuidv7();
    v_today    date;
BEGIN
    PERFORM inv.ensure_permission(p_company, 'STOCK_AUDIT', 'You are not allowed to audit stock.');
    IF p_client_transaction IS NULL THEN
        RAISE EXCEPTION 'a client transaction id is required' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('inv.client_transaction:' || p_client_transaction::text, 0));
    SELECT id INTO v_existing FROM inv.documents WHERE company_id = p_company AND document_uuid = p_client_transaction;
    IF v_existing IS NOT NULL THEN
        RETURN v_existing;
    END IF;
    IF p_store IS NULL OR p_store = c_head_office THEN
        RAISE EXCEPTION 'choose the store that is counted' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM 1 FROM core.stores WHERE company_id = p_company AND id = p_store AND is_active;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'the store was not found or is not active' USING ERRCODE = 'check_violation';
    END IF;
    SELECT (now() AT TIME ZONE c.timezone)::date INTO v_today FROM platform.companies c WHERE c.id = p_company;
    IF p_date IS NULL OR p_date > v_today THEN
        RAISE EXCEPTION 'the audit date cannot be in the future' USING ERRCODE = 'check_violation';
    END IF;
    INSERT INTO inv.documents (company_id, id, store_id, doc_type, doc_no, doc_date, status, reason, document_uuid, created_by)
    VALUES (p_company, v_document, p_store, 'AUDIT', core.next_number(p_company, p_store, 'STOCK_AUDIT'), p_date, 'DRAFT',
            NULLIF(btrim(p_note), ''), p_client_transaction, core.current_user_id());
    RETURN v_document;
END $$;

-- One scan call = one or more pieces of one barcode (p_quantity > 0), or a correction (p_quantity < 0). A repeated device scan id changes nothing.
-- Returns {"variantId": "...", "countedQuantity": n, "replayed": false}.
CREATE FUNCTION inv.record_audit_scan(p_company uuid, p_document uuid, p_barcode text, p_quantity integer, p_device_scan uuid) RETURNS jsonb
LANGUAGE plpgsql AS $$
DECLARE
    d          inv.documents%ROWTYPE;
    v_variant  uuid;
    v_counted  numeric;
    v_existing record;
BEGIN
    PERFORM inv.ensure_permission(p_company, 'STOCK_AUDIT', 'You are not allowed to audit stock.');
    IF p_device_scan IS NULL THEN
        RAISE EXCEPTION 'a scan id is required' USING ERRCODE = 'check_violation';
    END IF;
    IF p_quantity IS NULL OR p_quantity = 0 OR p_quantity NOT BETWEEN -1000 AND 1000 THEN
        RAISE EXCEPTION 'the scanned quantity must be between 1 and 1000 (or a correction down to -1000)' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('inv.audit_scan:' || p_device_scan::text, 0));
    SELECT s.variant_id INTO v_existing FROM inv.stock_audit_scans s WHERE s.company_id = p_company AND s.device_scan_uuid = p_device_scan;
    IF FOUND THEN
        SELECT COALESCE(sum(s.quantity), 0) INTO v_counted FROM inv.stock_audit_scans s
         WHERE s.company_id = p_company AND s.document_id = p_document AND s.variant_id = v_existing.variant_id;
        RETURN jsonb_build_object('variantId', v_existing.variant_id, 'countedQuantity', v_counted, 'replayed', true);
    END IF;

    SELECT * INTO d FROM inv.documents WHERE company_id = p_company AND id = p_document AND NOT is_deleted FOR UPDATE;
    IF NOT FOUND OR d.doc_type <> 'AUDIT' THEN
        RAISE EXCEPTION 'the stock audit was not found' USING ERRCODE = 'no_data_found';
    END IF;
    IF d.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'audit % is %, only an audit that is still being counted accepts scans', d.doc_no, d.status USING ERRCODE = 'check_violation';
    END IF;
    SELECT v.id INTO v_variant FROM catalog.variants v WHERE v.company_id = p_company AND v.barcode = btrim(p_barcode) AND NOT v.is_deleted;
    IF v_variant IS NULL THEN
        RAISE EXCEPTION 'barcode % is not a known article size', p_barcode USING ERRCODE = 'no_data_found';
    END IF;
    SELECT COALESCE(sum(s.quantity), 0) INTO v_counted FROM inv.stock_audit_scans s
     WHERE s.company_id = p_company AND s.document_id = p_document AND s.variant_id = v_variant;
    IF v_counted + p_quantity < 0 THEN
        RAISE EXCEPTION 'the count of barcode % is % and cannot go below zero', p_barcode, v_counted USING ERRCODE = 'check_violation';
    END IF;
    INSERT INTO inv.stock_audit_scans (company_id, document_id, store_id, barcode, variant_id, quantity, device_scan_uuid)
    VALUES (p_company, p_document, d.store_id, btrim(p_barcode), v_variant, p_quantity, p_device_scan);
    RETURN jsonb_build_object('variantId', v_variant, 'countedQuantity', v_counted + p_quantity, 'replayed', false);
END $$;

-- "Adjust Stock": posts counted minus book quantity for every counted size (and, when asked, books zero for sizes that were not scanned at all).
-- The book quantity is the stock on hand at this moment (read under the same lock that moves it). Posting twice returns the same audit.
CREATE FUNCTION inv.post_stock_audit(p_company uuid, p_document uuid, p_include_unscanned boolean) RETURNS uuid
LANGUAGE plpgsql AS $$
DECLARE
    d          inv.documents%ROWTYPE;
    v_today    date;
    v_lines    jsonb;
    v_counted  integer;
    v_matched  integer;
    v_value    numeric;
BEGIN
    PERFORM inv.ensure_permission(p_company, 'STOCK_AUDIT', 'You are not allowed to audit stock.');
    SELECT * INTO d FROM inv.documents WHERE company_id = p_company AND id = p_document AND NOT is_deleted FOR UPDATE;
    IF NOT FOUND OR d.doc_type <> 'AUDIT' THEN
        RAISE EXCEPTION 'the stock audit was not found' USING ERRCODE = 'no_data_found';
    END IF;
    IF d.status = 'POSTED' THEN
        RETURN d.id;
    END IF;
    IF d.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'audit % is %, only an audit that is still being counted can be posted', d.doc_no, d.status USING ERRCODE = 'check_violation';
    END IF;
    SELECT (now() AT TIME ZONE c.timezone)::date INTO v_today FROM platform.companies c WHERE c.id = p_company;

    -- lock the stock rows of everything this audit touches, in ascending size order, before reading the book quantities
    PERFORM 1 FROM inv.stock_balances b
     WHERE b.company_id = p_company AND b.store_id = d.store_id
       AND (b.variant_id IN (SELECT s.variant_id FROM inv.stock_audit_scans s WHERE s.company_id = p_company AND s.document_id = d.id)
            OR (COALESCE(p_include_unscanned, false) AND b.qty_on_hand > 0))
     ORDER BY b.variant_id FOR UPDATE;

    WITH counted AS (
        SELECT s.variant_id, sum(s.quantity)::numeric AS counted_qty
          FROM inv.stock_audit_scans s WHERE s.company_id = p_company AND s.document_id = d.id GROUP BY s.variant_id
    ), compared AS (
        SELECT c.variant_id, c.counted_qty, COALESCE(b.qty_on_hand, 0)::numeric AS expected_qty, true AS was_scanned
          FROM counted c LEFT JOIN inv.stock_balances b ON b.company_id = p_company AND b.store_id = d.store_id AND b.variant_id = c.variant_id
        UNION ALL
        SELECT b.variant_id, 0::numeric, b.qty_on_hand::numeric, false
          FROM inv.stock_balances b
         WHERE COALESCE(p_include_unscanned, false) AND b.company_id = p_company AND b.store_id = d.store_id AND b.qty_on_hand > 0
           AND NOT EXISTS (SELECT 1 FROM counted c WHERE c.variant_id = b.variant_id)
    )
    SELECT COALESCE(jsonb_agg(jsonb_build_object('variantId', x.variant_id, 'quantity', x.counted_qty - x.expected_qty,
                                                 'expectedQuantity', x.expected_qty, 'countedQuantity', x.counted_qty,
                                                 'reason', 'Stock audit ' || d.doc_no) ORDER BY x.variant_id)
                    FILTER (WHERE x.counted_qty <> x.expected_qty), '[]'::jsonb),
           count(*) FILTER (WHERE x.was_scanned),
           count(*) FILTER (WHERE x.was_scanned AND x.counted_qty = x.expected_qty)
      INTO v_lines, v_counted, v_matched
      FROM compared x;

    v_value := inv.apply_document_lines(p_company, d.store_id, d.id, v_today, 'AUDIT', v_lines);
    PERFORM inv.post_stock_variance_journal(p_company, d.store_id, v_today, 'STOCK_AUDIT', d.id, 'Stock audit ' || d.doc_no, v_value);
    INSERT INTO inv.document_extras (company_id, document_id, store_id, kind, counted_item_count, matched_item_count, includes_unscanned_items)
    VALUES (p_company, d.id, d.store_id, 'AUDIT_ADJUSTMENT', v_counted, v_matched, COALESCE(p_include_unscanned, false));
    UPDATE inv.documents SET status = 'POSTED', posted_at = now(), posted_by = core.current_user_id() WHERE company_id = p_company AND id = d.id;
    RETURN d.id;
END $$;

-- "Delete" of an audit: only while it is still being counted. A posted audit is history and stays.
CREATE FUNCTION inv.cancel_stock_audit(p_company uuid, p_document uuid) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE
    d inv.documents%ROWTYPE;
BEGIN
    PERFORM inv.ensure_permission(p_company, 'STOCK_AUDIT', 'You are not allowed to audit stock.');
    SELECT * INTO d FROM inv.documents WHERE company_id = p_company AND id = p_document AND NOT is_deleted FOR UPDATE;
    IF NOT FOUND OR d.doc_type <> 'AUDIT' THEN
        RAISE EXCEPTION 'the stock audit was not found' USING ERRCODE = 'no_data_found';
    END IF;
    IF d.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'audit % is %, only an audit that is still being counted can be deleted', d.doc_no, d.status USING ERRCODE = 'check_violation';
    END IF;
    UPDATE inv.documents SET status = 'CANCELLED' WHERE company_id = p_company AND id = d.id;
END $$;

-- ---------------------------------------------------------------------
-- 5. Product claim request (docx 4.16). Approval and rejection are inv.approve_claim / inv.reject_claim (baseline).
-- ---------------------------------------------------------------------
CREATE FUNCTION inv.request_claim(p_company uuid, p_store uuid, p_date date, p_client_transaction uuid, p_variant uuid, p_qty numeric,
                                  p_reason text, p_note text) RETURNS uuid
LANGUAGE plpgsql AS $$
DECLARE
    c_head_office constant uuid := '00000000-0000-0000-0000-000000000000';
    v_existing uuid;
    v_claim    uuid := uuidv7();
    v_today    date;
BEGIN
    PERFORM inv.ensure_permission(p_company, 'CLAIM_REQUEST', 'You are not allowed to raise product claims.');
    IF p_client_transaction IS NULL THEN
        RAISE EXCEPTION 'a client transaction id is required' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('inv.client_transaction:' || p_client_transaction::text, 0));
    SELECT entity_id INTO v_existing FROM inv.client_transactions WHERE company_id = p_company AND client_transaction_id = p_client_transaction;
    IF v_existing IS NOT NULL THEN
        RETURN v_existing;
    END IF;
    IF p_store IS NULL OR p_store = c_head_office THEN
        RAISE EXCEPTION 'choose the store of the claim' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM 1 FROM core.stores WHERE company_id = p_company AND id = p_store AND is_active;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'the store was not found or is not active' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM 1 FROM catalog.variants WHERE company_id = p_company AND id = p_variant AND NOT is_deleted;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'the size was not found or has been deleted' USING ERRCODE = 'check_violation';
    END IF;
    IF p_qty IS NULL OR p_qty <= 0 THEN
        RAISE EXCEPTION 'the quantity of a claim must be more than zero' USING ERRCODE = 'check_violation';
    END IF;
    SELECT (now() AT TIME ZONE c.timezone)::date INTO v_today FROM platform.companies c WHERE c.id = p_company;
    IF p_date IS NULL OR p_date > v_today THEN
        RAISE EXCEPTION 'the claim date cannot be in the future' USING ERRCODE = 'check_violation';
    END IF;
    INSERT INTO inv.claims (company_id, id, store_id, claim_no, claim_date, variant_id, qty, reason, note, requested_by, created_by)
    VALUES (p_company, v_claim, p_store, core.next_number(p_company, p_store, 'PRODUCT_CLAIM'), p_date, p_variant, p_qty,
            p_reason, NULLIF(btrim(p_note), ''), core.current_user_id(), core.current_user_id());
    INSERT INTO inv.client_transactions (company_id, client_transaction_id, entity_type, entity_id)
    VALUES (p_company, p_client_transaction, 'CLAIM', v_claim);
    RETURN v_claim;
END $$;

-- ---------------------------------------------------------------------
-- 6. Transfers (docx 5.9, 5.10; Part 19)
-- ---------------------------------------------------------------------
CREATE FUNCTION inv.create_transfer(p_company uuid, p_from uuid, p_to uuid, p_client_transaction uuid, p_note text) RETURNS uuid
LANGUAGE plpgsql AS $$
DECLARE
    c_head_office constant uuid := '00000000-0000-0000-0000-000000000000';
    v_existing uuid;
    v_transfer uuid := uuidv7();
BEGIN
    PERFORM inv.ensure_permission(p_company, 'TRANSFER_CREATE', 'You are not allowed to create stock transfers.');
    IF p_client_transaction IS NULL THEN
        RAISE EXCEPTION 'a client transaction id is required' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('inv.client_transaction:' || p_client_transaction::text, 0));
    SELECT id INTO v_existing FROM inv.transfers WHERE company_id = p_company AND document_uuid = p_client_transaction;
    IF v_existing IS NOT NULL THEN
        RETURN v_existing;
    END IF;
    IF p_from IS NULL OR p_to IS NULL OR p_from = c_head_office OR p_to = c_head_office THEN
        RAISE EXCEPTION 'choose the sending and the receiving store' USING ERRCODE = 'check_violation';
    END IF;
    IF p_from = p_to THEN
        RAISE EXCEPTION 'the sending and the receiving store must be different' USING ERRCODE = 'check_violation';
    END IF;
    IF (SELECT count(*) FROM core.stores WHERE company_id = p_company AND id IN (p_from, p_to) AND is_active) <> 2 THEN
        RAISE EXCEPTION 'both stores must be active stores of this company' USING ERRCODE = 'check_violation';
    END IF;
    INSERT INTO inv.transfers (company_id, id, transfer_no, from_store_id, to_store_id, status, note, document_uuid, created_by)
    VALUES (p_company, v_transfer, core.next_number(p_company, p_from, 'STOCK_TRANSFER'), p_from, p_to, 'DRAFT',
            NULLIF(btrim(p_note), ''), p_client_transaction, core.current_user_id());
    RETURN v_transfer;
END $$;

-- p_phase SEND (sender, while DRAFT) or RECEIVE (receiver, after dispatch). One call scans p_quantity pieces of one barcode: every piece is one row of
-- inv.transfer_scans with its own scan id (derived from p_scan_batch), so a retried call (same p_scan_batch) records nothing twice. All or nothing.
-- Returns {"variantId": "...", "quantityScanned": n, "quantityReceived": n, "replayed": false}.
CREATE FUNCTION inv.record_transfer_scan(p_company uuid, p_transfer uuid, p_store uuid, p_phase text, p_barcode text, p_quantity integer,
                                         p_scan_batch uuid) RETURNS jsonb
LANGUAGE plpgsql AS $$
DECLARE
    v_variant  uuid;
    v_first    uuid;
    v_piece    integer;
    v_replayed boolean := false;
    v_line     inv.transfer_lines%ROWTYPE;
BEGIN
    IF p_phase = 'SEND' THEN
        PERFORM inv.ensure_permission(p_company, 'TRANSFER_CREATE', 'You are not allowed to create stock transfers.');
    ELSIF p_phase = 'RECEIVE' THEN
        PERFORM inv.ensure_permission(p_company, 'TRANSFER_RECEIVE', 'You are not allowed to receive stock transfers.');
    ELSE
        RAISE EXCEPTION 'unknown scan phase %', p_phase USING ERRCODE = 'check_violation';
    END IF;
    IF p_scan_batch IS NULL THEN
        RAISE EXCEPTION 'a scan id is required' USING ERRCODE = 'check_violation';
    END IF;
    IF p_quantity IS NULL OR p_quantity NOT BETWEEN 1 AND 200 THEN
        RAISE EXCEPTION 'scan between 1 and 200 pieces at a time' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('inv.transfer_scan:' || p_scan_batch::text, 0));
    v_first := md5(p_scan_batch::text || ':1')::uuid;
    IF EXISTS (SELECT 1 FROM inv.transfer_scans WHERE company_id = p_company AND device_scan_uuid = v_first) THEN
        v_replayed := true;
    ELSE
        FOR v_piece IN 1..p_quantity LOOP
            INSERT INTO inv.transfer_scans (company_id, transfer_id, store_id, phase, barcode, device_scan_uuid, scanned_by)
            VALUES (p_company, p_transfer, p_store, p_phase, btrim(p_barcode), md5(p_scan_batch::text || ':' || v_piece)::uuid, core.current_user_id());
        END LOOP;
    END IF;
    SELECT v.id INTO v_variant FROM catalog.variants v WHERE v.company_id = p_company AND v.barcode = btrim(p_barcode) AND NOT v.is_deleted;
    SELECT * INTO v_line FROM inv.transfer_lines l WHERE l.company_id = p_company AND l.transfer_id = p_transfer AND l.variant_id = v_variant;
    RETURN jsonb_build_object('variantId', v_variant, 'quantityScanned', COALESCE(v_line.qty_scanned, 0),
                              'quantityReceived', COALESCE(v_line.qty_received, 0), 'replayed', v_replayed);
END $$;

-- A DRAFT that is not wanted any more. Nothing was moved, so nothing is reversed; the scans and the event stay as history.
CREATE FUNCTION inv.cancel_transfer(p_company uuid, p_transfer uuid, p_reason text) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE
    t inv.transfers%ROWTYPE;
BEGIN
    PERFORM inv.ensure_permission(p_company, 'TRANSFER_CREATE', 'You are not allowed to create stock transfers.');
    SELECT * INTO t FROM inv.transfers WHERE company_id = p_company AND id = p_transfer AND NOT is_deleted FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'the transfer was not found' USING ERRCODE = 'no_data_found';
    END IF;
    IF t.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'transfer % is %, only a draft can be cancelled', t.transfer_no, t.status USING ERRCODE = 'check_violation';
    END IF;
    UPDATE inv.transfers SET status = 'CANCELLED' WHERE company_id = p_company AND id = t.id;
    INSERT INTO inv.transfer_events (company_id, transfer_id, event_type, reason, user_id)
    VALUES (p_company, t.id, 'CANCEL', NULLIF(btrim(p_reason), ''), core.current_user_id());
END $$;

-- ---------------------------------------------------------------------
-- 7. BASELINE FIX: inv.decide_loss_case (the integer 0 in the CASE was compared with uuids; CHARGE failed for every bearer)
--    Identical to the baseline except for the one expression marked below.
-- ---------------------------------------------------------------------
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
        -- FIX: the company bearer is the head office, whose store id is the nil uuid (the baseline wrote the integer 0 here)
        v_store := CASE p_bearer WHEN 'COMPANY' THEN '00000000-0000-0000-0000-000000000000'::uuid WHEN 'SENDER_STORE' THEN t.from_store_id ELSE t.to_store_id END;
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

-- ---------------------------------------------------------------------
-- 7b. BASELINE FIX: an approved claim had to carry a journal entry, but inv.approve_claim writes none when the stock has no cost (value 0: opening stock
--     without a purchase price), so approving such a claim failed on the table check. A claim at zero cost needs no journal entry; every other approved
--     claim still must have one.
-- ---------------------------------------------------------------------
ALTER TABLE inv.claims DROP CONSTRAINT claims_check;
ALTER TABLE inv.claims ADD CONSTRAINT claims_approved_has_decider_and_journal
    CHECK (status <> 'APPROVED' OR (decided_by IS NOT NULL AND (journal_entry_id IS NOT NULL OR COALESCE(unit_cost, 0) = 0)));

-- ---------------------------------------------------------------------
-- 7c. BASELINE FIX: sending, receiving and deciding a loss of a transfer book one leg on the head office TRANSIT account (journal line store = the head office
--     store). The store-level row security of the journal tables refused that line for every person who works in only some stores (only a person with all
--     stores could send or receive; the check that an entry balances could not even see the head office line). The tables now accept a head office line only inside the transfer functions, which mark their transaction with
--     app.internal_transfer_write (the same marker the baseline already uses to guard the transfer tables). Outside those transactions nothing changes: a store-level person still sees
--     only the lines of their own stores. (The month totals table is updated with INSERT ... ON CONFLICT DO UPDATE, so its policy lets the transfer functions see the
--     head office row they are adding to.)
-- ---------------------------------------------------------------------
DROP POLICY store_isolation ON acct.journal_lines;
CREATE POLICY store_isolation ON acct.journal_lines AS RESTRICTIVE
    USING (core.store_allowed(store_id)
           OR (store_id = '00000000-0000-0000-0000-000000000000'::uuid
               AND COALESCE(current_setting('app.internal_transfer_write', true), '') = 'on'))
    WITH CHECK (core.store_allowed(store_id)
                OR (store_id = '00000000-0000-0000-0000-000000000000'::uuid
                    AND COALESCE(current_setting('app.internal_transfer_write', true), '') = 'on'));

DROP POLICY store_isolation ON acct.account_month_balances;
CREATE POLICY store_isolation ON acct.account_month_balances AS RESTRICTIVE
    USING (core.store_allowed(store_id)
           OR (store_id = '00000000-0000-0000-0000-000000000000'::uuid
               AND COALESCE(current_setting('app.internal_transfer_write', true), '') = 'on'))
    WITH CHECK (core.store_allowed(store_id)
                OR (store_id = '00000000-0000-0000-0000-000000000000'::uuid
                    AND COALESCE(current_setting('app.internal_transfer_write', true), '') = 'on'));

-- ---------------------------------------------------------------------
-- 8. Grants
-- ---------------------------------------------------------------------
GRANT EXECUTE ON FUNCTION inv.ensure_permission(uuid, text, text) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.stock_unit_cost(uuid, uuid, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.post_stock_variance_journal(uuid, uuid, date, text, uuid, text, numeric) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.apply_document_lines(uuid, uuid, uuid, date, text, jsonb) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.post_stock_adjustment(uuid, uuid, date, uuid, text, jsonb, text) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.void_stock_adjustment(uuid, uuid, text, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.create_stock_audit(uuid, uuid, date, uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.record_audit_scan(uuid, uuid, text, integer, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.post_stock_audit(uuid, uuid, boolean) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.cancel_stock_audit(uuid, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.request_claim(uuid, uuid, date, uuid, uuid, numeric, text, text) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.create_transfer(uuid, uuid, uuid, uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.record_transfer_scan(uuid, uuid, uuid, text, text, integer, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.cancel_transfer(uuid, uuid, text) TO pos_app;
