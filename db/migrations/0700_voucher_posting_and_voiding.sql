-- 0700 Accounting: vouchers (Phase 0 rows ORG-6.1 to ORG-6.6, ADD-15-01 to ADD-15-05).
--
--   1. acct.vouchers.client_transaction_id: a retried request (same Guid from the device or browser) returns the voucher that
--      already exists instead of posting a second journal entry (Part 23, Part 26 idempotency).
--   2. Store isolation for acct.vouchers and acct.voucher_allocations. The baseline only isolated them by company, so a store
--      user could read every store's vouchers; their journal lines were already store-isolated.
--   3. acct.resolve_voucher_side, acct.post_voucher, acct.void_voucher: the ONLY way vouchers are written. The application never
--      inserts journal rows itself (Part 15); the deferred trigger acct.check_entry_balanced still proves every entry balances.
--   4. purch.v_invoice_balance no longer counts allocations of VOID vouchers as payments. The baseline view summed all allocations,
--      so voiding a payment voucher would have left the purchase invoice looking paid.
-- Accounting treatment: a voucher is one journal entry with two lines (debit one account, credit another), as the docx
-- shows (two grid rows per voucher). Lines carry the voucher's store, like every other posting function in the schema.
-- Which account types may be combined on which voucher type is a business policy ([BUSINESS DECISION REQUIRED]) and is NOT
-- enforced here; only technical integrity rules are (stock accounts are posted by stock documents, parties go through the party
-- control account, debit and credit must differ).
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

ALTER TABLE acct.vouchers ADD COLUMN client_transaction_id uuid;
CREATE UNIQUE INDEX ux_vouchers_client_transaction
    ON acct.vouchers (company_id, client_transaction_id) WHERE client_transaction_id IS NOT NULL;

CREATE POLICY store_isolation ON acct.vouchers AS RESTRICTIVE
    USING (core.store_allowed(store_id)) WITH CHECK (core.store_allowed(store_id));
CREATE POLICY store_isolation ON acct.voucher_allocations AS RESTRICTIVE
    USING (core.store_allowed(store_id)) WITH CHECK (core.store_allowed(store_id));

CREATE OR REPLACE VIEW purch.v_invoice_balance WITH (security_invoker = true) AS
SELECT i.company_id, i.id AS invoice_id, i.store_id, i.vendor_party_id, i.bill_date, i.due_date,
       i.net_amount, COALESCE(a.paid, 0) AS paid_amount,
       i.net_amount - COALESCE(a.paid, 0) AS remaining_amount
  FROM purch.invoices i
  LEFT JOIN (SELECT va.company_id, va.purchase_invoice_id, SUM(va.amount) AS paid
               FROM acct.voucher_allocations va
               JOIN acct.vouchers v ON v.company_id = va.company_id AND v.id = va.voucher_id AND v.status = 'POSTED'
              WHERE va.purchase_invoice_id IS NOT NULL
              GROUP BY va.company_id, va.purchase_invoice_id) a
         ON a.company_id = i.company_id AND a.purchase_invoice_id = i.id
 WHERE i.doc_type = 'PURCHASE' AND i.status = 'POSTED';

-- The baseline grants pos_app table access before its views exist and grants only two views afterwards, so the application role could not
-- read this view at all. acct.post_voucher reads it to check allocations (the purchasing module reads it for vendor balances).
GRANT SELECT ON purch.v_invoice_balance TO pos_app;

-- ---------------------------------------------------------------------
-- One side of a voucher: a party (posted to the party control account with the party id on the line) or a plain account.
-- Raises check_violation (the API answers 422) for everything the caller can fix.
-- ---------------------------------------------------------------------
CREATE FUNCTION acct.resolve_voucher_side(p_company uuid, p_account uuid, p_party uuid, p_side text)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    v_control uuid;
    v_account acct.accounts%ROWTYPE;
BEGIN
    IF p_party IS NOT NULL THEN
        PERFORM 1 FROM party.parties p WHERE p.company_id = p_company AND p.id = p_party AND NOT p.is_deleted;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'party % was not found (% side)', p_party, p_side USING ERRCODE = 'check_violation';
        END IF;
        v_control := acct.role_account(p_company, 'PARTY_CONTROL');
        IF p_account IS NOT NULL AND p_account <> v_control THEN
            RAISE EXCEPTION 'a party on the % side is posted to the party control account: do not name another account', p_side
                USING ERRCODE = 'check_violation';
        END IF;
        RETURN v_control;
    END IF;

    IF p_account IS NULL THEN
        RAISE EXCEPTION 'name an account or a party on the % side', p_side USING ERRCODE = 'check_violation';
    END IF;
    SELECT * INTO v_account FROM acct.accounts a WHERE a.company_id = p_company AND a.id = p_account;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'account % was not found (% side)', p_account, p_side USING ERRCODE = 'check_violation';
    END IF;
    IF NOT v_account.is_active OR NOT v_account.is_postable THEN
        RAISE EXCEPTION 'account % cannot be posted to (inactive or a heading account)', v_account.code USING ERRCODE = 'check_violation';
    END IF;
    IF v_account.is_party_control THEN
        RAISE EXCEPTION 'account % is the party control account: choose the party instead', v_account.code USING ERRCODE = 'check_violation';
    END IF;
    IF v_account.system_role IN ('STOCK', 'TRANSIT') THEN
        RAISE EXCEPTION 'account % (%) is posted by stock documents, not by vouchers', v_account.code, v_account.system_role
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN v_account.id;
END $$;

-- ---------------------------------------------------------------------
-- Posts a voucher: journal entry + two lines + voucher row (+ allocations) in the caller's transaction.
-- p_allocations: jsonb array [{"invoiceId": "...", "amount": 1000}]. A PAYMENT to a vendor settles purchase invoices of that
-- vendor, a RECEIPT from a customer settles credit sale invoices of that customer; the rest of the amount stays on account.
-- Returns the voucher id. A repeated p_client_transaction returns the voucher posted the first time.
-- ---------------------------------------------------------------------
CREATE FUNCTION acct.post_voucher(p_company uuid, p_store uuid, p_type text, p_date date,
                                  p_debit_account uuid, p_debit_party uuid,
                                  p_credit_account uuid, p_credit_party uuid,
                                  p_amount core.money, p_payment_mode text, p_ref_no text, p_description text,
                                  p_client_transaction uuid, p_allocations jsonb DEFAULT NULL)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    v_existing      uuid;
    v_debit         uuid;
    v_credit        uuid;
    v_voucher       uuid := uuidv7();
    v_entry         uuid;
    v_number        bigint;
    v_date          date;
    v_line          int := 1;
    v_narration     text;
    v_item          jsonb;
    v_invoice       uuid;
    v_alloc         bigint;
    v_alloc_total   bigint := 0;
    v_remaining     bigint;
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'VOUCHER_POST');

    IF p_type IS NULL OR p_type NOT IN ('PAYMENT', 'RECEIPT', 'EXPENSE', 'JOURNAL') THEN
        RAISE EXCEPTION 'voucher type % is not valid', p_type USING ERRCODE = 'check_violation';
    END IF;
    IF p_amount IS NULL OR p_amount <= 0 THEN
        RAISE EXCEPTION 'the voucher amount must be more than zero' USING ERRCODE = 'check_violation';
    END IF;

    IF p_client_transaction IS NOT NULL THEN
        SELECT v.id INTO v_existing FROM acct.vouchers v
         WHERE v.company_id = p_company AND v.client_transaction_id = p_client_transaction;
        IF FOUND THEN RETURN v_existing; END IF;
    END IF;

    PERFORM 1 FROM core.stores s WHERE s.company_id = p_company AND s.id = p_store AND s.is_active;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'store % was not found or is not active', p_store USING ERRCODE = 'check_violation';
    END IF;
    IF NOT core.store_allowed(p_store) THEN
        RAISE EXCEPTION 'you do not have access to store %', p_store USING ERRCODE = 'insufficient_privilege';
    END IF;

    v_debit  := acct.resolve_voucher_side(p_company, p_debit_account,  p_debit_party,  'debit');
    v_credit := acct.resolve_voucher_side(p_company, p_credit_account, p_credit_party, 'credit');
    IF v_debit = v_credit THEN
        RAISE EXCEPTION 'debit and credit must be different (two parties cannot be posted against each other: use an account in between)'
            USING ERRCODE = 'check_violation';
    END IF;

    v_date := COALESCE(p_date, CURRENT_DATE);
    v_number := core.next_number(p_company, p_store, p_type || '_VOUCHER');
    v_narration := initcap(lower(p_type)) || ' voucher ' || v_number
                   || COALESCE(': ' || NULLIF(btrim(p_description), ''), '');

    INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
    VALUES (p_company, v_date, p_store, 'VOUCHER', v_voucher, v_narration, core.current_user_id())
    RETURNING id INTO v_entry;
    v_line := acct.add_line(p_company, v_entry, v_date, v_line, p_store, v_debit,  p_debit_party,  p_amount, 0, p_description);
    v_line := acct.add_line(p_company, v_entry, v_date, v_line, p_store, v_credit, p_credit_party, 0, p_amount, p_description);

    INSERT INTO acct.vouchers (company_id, id, store_id, voucher_type, voucher_no, voucher_date, debit_account_id, credit_account_id,
                               amount, party_id, payment_mode, ref_no, description, journal_entry_id, client_transaction_id, created_by)
    VALUES (p_company, v_voucher, p_store, p_type, v_number, v_date, v_debit, v_credit,
            p_amount, COALESCE(p_debit_party, p_credit_party), p_payment_mode, NULLIF(btrim(p_ref_no), ''),
            NULLIF(btrim(p_description), ''), v_entry, p_client_transaction, core.current_user_id());

    IF p_allocations IS NOT NULL AND jsonb_typeof(p_allocations) = 'array' AND jsonb_array_length(p_allocations) > 0 THEN
        IF NOT ((p_type = 'PAYMENT' AND p_debit_party IS NOT NULL) OR (p_type = 'RECEIPT' AND p_credit_party IS NOT NULL)) THEN
            RAISE EXCEPTION 'allocations apply only to a payment to a vendor or a receipt from a customer' USING ERRCODE = 'check_violation';
        END IF;
        FOR v_item IN SELECT * FROM jsonb_array_elements(p_allocations) LOOP
            v_invoice := (v_item ->> 'invoiceId')::uuid;
            v_alloc   := (v_item ->> 'amount')::bigint;
            IF v_invoice IS NULL OR v_alloc IS NULL OR v_alloc <= 0 THEN
                RAISE EXCEPTION 'every allocation needs an invoice and an amount above zero' USING ERRCODE = 'check_violation';
            END IF;

            IF p_type = 'PAYMENT' THEN
                PERFORM 1 FROM purch.invoices i
                 WHERE i.company_id = p_company AND i.id = v_invoice FOR UPDATE;
                SELECT b.remaining_amount INTO v_remaining FROM purch.v_invoice_balance b
                 WHERE b.company_id = p_company AND b.invoice_id = v_invoice AND b.vendor_party_id = p_debit_party;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'purchase invoice % is not an open invoice of this vendor', v_invoice USING ERRCODE = 'check_violation';
                END IF;
                IF v_alloc > v_remaining THEN
                    RAISE EXCEPTION 'allocation % is more than the remaining % of purchase invoice %', v_alloc, v_remaining, v_invoice
                        USING ERRCODE = 'check_violation';
                END IF;
                INSERT INTO acct.voucher_allocations (company_id, voucher_id, store_id, purchase_invoice_id, amount)
                VALUES (p_company, v_voucher, p_store, v_invoice, v_alloc);
            ELSE
                PERFORM 1 FROM sales.invoices i
                 WHERE i.company_id = p_company AND i.id = v_invoice FOR UPDATE;
                SELECT i.credit_amount - COALESCE((SELECT SUM(va.amount)
                                                     FROM acct.voucher_allocations va
                                                     JOIN acct.vouchers v ON v.company_id = va.company_id AND v.id = va.voucher_id AND v.status = 'POSTED'
                                                    WHERE va.company_id = i.company_id AND va.sale_invoice_id = i.id), 0)
                  INTO v_remaining
                  FROM sales.invoices i
                 WHERE i.company_id = p_company AND i.id = v_invoice AND i.doc_type = 'SALE' AND i.status = 'POSTED'
                   AND i.customer_party_id = p_credit_party;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'sale invoice % is not a credit sale of this customer', v_invoice USING ERRCODE = 'check_violation';
                END IF;
                IF v_alloc > v_remaining THEN
                    RAISE EXCEPTION 'allocation % is more than the remaining % of sale invoice %', v_alloc, v_remaining, v_invoice
                        USING ERRCODE = 'check_violation';
                END IF;
                INSERT INTO acct.voucher_allocations (company_id, voucher_id, store_id, sale_invoice_id, amount)
                VALUES (p_company, v_voucher, p_store, v_invoice, v_alloc);
            END IF;
            v_alloc_total := v_alloc_total + v_alloc;
        END LOOP;
        IF v_alloc_total > p_amount THEN
            RAISE EXCEPTION 'the allocations total % but the voucher is only %', v_alloc_total, p_amount USING ERRCODE = 'check_violation';
        END IF;
    END IF;

    RETURN v_voucher;
EXCEPTION
    WHEN unique_violation THEN
        -- A concurrent request with the same client transaction id won the race: return its voucher.
        IF p_client_transaction IS NOT NULL THEN
            SELECT v.id INTO v_existing FROM acct.vouchers v
             WHERE v.company_id = p_company AND v.client_transaction_id = p_client_transaction;
            IF FOUND THEN RETURN v_existing; END IF;
        END IF;
        RAISE;
END $$;

-- ---------------------------------------------------------------------
-- Voids a voucher: the journal entry is reversed (acct.reverse_entry), the voucher keeps its row with status VOID, the reason and
-- who/when; nothing is deleted. The reversal is dated today, so a closed month of the original entry does not block the correction.
-- ---------------------------------------------------------------------
CREATE FUNCTION acct.void_voucher(p_company uuid, p_voucher uuid, p_reason text)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    v_voucher acct.vouchers%ROWTYPE;
    v_reversal uuid;
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'VOUCHER_DELETE');
    IF p_reason IS NULL OR btrim(p_reason) = '' THEN
        RAISE EXCEPTION 'a reason is needed to void a voucher' USING ERRCODE = 'check_violation';
    END IF;

    SELECT * INTO v_voucher FROM acct.vouchers v WHERE v.company_id = p_company AND v.id = p_voucher FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'voucher % was not found', p_voucher USING ERRCODE = 'check_violation';
    END IF;
    IF v_voucher.status = 'VOID' THEN
        RAISE EXCEPTION 'voucher % is already void', v_voucher.voucher_no USING ERRCODE = 'check_violation';
    END IF;

    v_reversal := acct.reverse_entry(p_company, v_voucher.journal_entry_id, 'VOUCHER_VOID', v_voucher.id,
                                     'Void of ' || lower(v_voucher.voucher_type) || ' voucher ' || v_voucher.voucher_no);
    UPDATE acct.vouchers
       SET status = 'VOID', void_reason = btrim(p_reason), voided_by = core.current_user_id(), voided_at = now()
     WHERE company_id = p_company AND id = p_voucher;
    RETURN v_reversal;
END $$;

GRANT EXECUTE ON FUNCTION acct.resolve_voucher_side(uuid, uuid, uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION acct.post_voucher(uuid, uuid, text, date, uuid, uuid, uuid, uuid, core.money, text, text, text, uuid, jsonb) TO pos_app;
GRANT EXECUTE ON FUNCTION acct.void_voucher(uuid, uuid, text) TO pos_app;
