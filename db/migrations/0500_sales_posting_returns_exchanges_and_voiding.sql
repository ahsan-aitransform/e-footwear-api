-- 0500 Sales: New Sale, Sale Return, Exchange Invoice, View Invoices, View Deleted Invoices (Phase 0 rows ORG-5.1-a..h, ORG-5.2, ORG-5.3,
-- ORG-5.13, ADD-11-01..04, ADD-12-01..05; PROJECT_REQUIREMENTS Part 11 and Part 12).
--
-- What the baseline already does (not repeated here): the journal entry of a sale or return is posted by the deferred trigger
-- sales.post_invoice_journal at COMMIT, the commission is accrued and reversed by triggers, the quantity of a return is limited by
-- sales.check_return_quantity, price bands are assigned by catalog.assign_price_band, and a void reverses the journal entry.
--
-- What this migration adds:
--   1. sales.invoice_extras / sales.invoice_line_extras: the invoice note (docx "Description"), the link of a corrected invoice to the one it
--      replaced, and the return reason and item condition of a return line (Part 12). The baseline has no columns for them. Append-only.
--   2. sales.post_invoice_journal now debits the payment account named on each payment row (docx "Payment account": cash in hand, a bank
--      account ...). The baseline always debited the head-office CASH role account, so a bank payment would have been booked as cash.
--      An invoice WITHOUT payment rows is posted exactly as before (CASH role account), so nothing that already inserts invoices changes.
--   3. sales.post_sale_invoice, sales.post_return_invoice, sales.post_exchange, sales.void_invoice: the ONLY way sales documents are written.
--      The application never inserts invoice, line or stock rows itself (Part 15, Part 26): the functions compute the lines, take the stock
--      out or put it back through inv.post_stock_move, number the document, and let the baseline triggers post the journal.
--   4. Idempotency (Part 23): the client transaction id is stored as sales.invoices.device_sale_uuid (unique per company). A repeated request
--      returns the invoice that already exists; an advisory lock makes two simultaneous identical requests post once.
--
-- Open business decisions, deliberately NOT decided here ([BUSINESS DECISION REQUIRED]): tax calculation (a product with a tax rate is refused),
-- discount stacking, cash refunds on returns (the baseline refuses them: a return credits the customer account), whether and when an invoice
-- may be edited or voided, returns at another store than the one that sold the goods, the effect of "damaged" goods on stock.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

-- ---------------------------------------------------------------------
-- 1. Extra facts the baseline tables have no column for
-- ---------------------------------------------------------------------
CREATE TABLE sales.invoice_extras (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    invoice_id          uuid NOT NULL,
    store_id            uuid NOT NULL,
    description         text CHECK (description IS NULL OR length(description) <= 500),
    replaces_invoice_id uuid,                                   -- "void and re-enter": the invoice this one corrects
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid DEFAULT core.current_user_id(),
    PRIMARY KEY (company_id, invoice_id),
    FOREIGN KEY (company_id, invoice_id)          REFERENCES sales.invoices(company_id, id),
    FOREIGN KEY (company_id, replaces_invoice_id) REFERENCES sales.invoices(company_id, id),
    FOREIGN KEY (company_id, store_id)            REFERENCES core.stores(company_id, id)
);

CREATE TABLE sales.invoice_line_extras (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    line_id         uuid NOT NULL,
    line_sale_date  date NOT NULL,
    store_id        uuid NOT NULL,
    return_reason   text CHECK (return_reason IS NULL OR length(return_reason) <= 200),
    item_condition  text NOT NULL CHECK (item_condition IN ('SELLABLE', 'DAMAGED')),
    PRIMARY KEY (company_id, line_id, line_sale_date),
    FOREIGN KEY (company_id, line_id, line_sale_date) REFERENCES sales.invoice_lines(company_id, id, sale_date),
    FOREIGN KEY (company_id, store_id)                REFERENCES core.stores(company_id, id)
);

CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON sales.invoice_extras
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON sales.invoice_line_extras
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();

SELECT core.enable_rls('sales.invoice_extras'::regclass, true);
SELECT core.enable_rls('sales.invoice_line_extras'::regclass, true);
GRANT SELECT, INSERT ON sales.invoice_extras, sales.invoice_line_extras TO pos_app;

-- ---------------------------------------------------------------------
-- 2. Permission check with a message a person can read (sec.require_permission raises a bare "user <id> lacks permission <code>")
-- ---------------------------------------------------------------------
CREATE FUNCTION sales.ensure_permission(p_company uuid, p_code text, p_message text) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), p_code);
EXCEPTION WHEN insufficient_privilege THEN
    RAISE EXCEPTION '%', p_message USING ERRCODE = 'insufficient_privilege';
END $$;

-- ---------------------------------------------------------------------
-- 3. The journal of a sale: the cash side goes to the payment accounts of the payment rows
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION sales.post_invoice_journal() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    h          sales.invoices%ROWTYPE;
    v_lines    int;
    v_net      core.money;
    v_tax      core.money;
    v_cost     core.money;
    v_entry    uuid;
    v_no       int := 1;
    v_cash     core.money;
    v_credit   core.money;
    v_amt      core.money;     -- sales value, taxes excluded (positive for both SALE and RETURN)
    v_tx       core.money;     -- tax (positive)
    v_k        core.money;     -- cost (positive)
    v_customer uuid;
    v_pay      record;
    v_pay_total core.money;
    v_pay_rows int;
BEGIN
    SELECT * INTO h FROM sales.invoices WHERE company_id = NEW.company_id AND id = NEW.id;
    IF h.status <> 'POSTED' OR h.journal_entry_id IS NOT NULL THEN
        RETURN NULL;
    END IF;

    SELECT count(*), COALESCE(sum(net_amount), 0), COALESCE(sum(tax_amount), 0), COALESCE(sum(cost_amount), 0)
      INTO v_lines, v_net, v_tax, v_cost
      FROM sales.invoice_lines WHERE company_id = h.company_id AND invoice_id = h.id;
    IF v_lines = 0 THEN
        RAISE EXCEPTION 'invoice % has no lines', h.id USING ERRCODE = 'check_violation';
    END IF;
    IF v_net <> h.net_amount OR v_tax <> h.tax_amount OR v_cost <> h.cost_amount THEN
        RAISE EXCEPTION 'invoice % header does not match its lines: net %/%, tax %/%, cost %/%',
            h.invoice_no, h.net_amount, v_net, h.tax_amount, v_tax, h.cost_amount, v_cost
            USING ERRCODE = 'check_violation';
    END IF;

    IF h.doc_type = 'SALE' THEN
        v_cash := h.paid_amount - h.change_amount;
        v_credit := h.net_amount - v_cash;
        IF v_cash < 0 OR v_credit < 0 THEN
            RAISE EXCEPTION 'invoice % payment % does not match net %', h.invoice_no, h.paid_amount, h.net_amount
                USING ERRCODE = 'check_violation';
        END IF;
        IF v_credit <> h.credit_amount THEN
            RAISE EXCEPTION 'invoice % unpaid amount % does not match credit amount %', h.invoice_no, v_credit, h.credit_amount
                USING ERRCODE = 'check_violation';
        END IF;
        IF v_credit > 0 AND h.customer_party_id IS NULL THEN
            RAISE EXCEPTION 'invoice % is a credit sale and needs a customer', h.invoice_no USING ERRCODE = 'check_violation';
        END IF;
        v_amt := h.net_amount - h.tax_amount;
        v_tx := h.tax_amount;
        v_k := h.cost_amount;

        INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
        VALUES (h.company_id, h.sale_date, h.store_id, 'SALE', h.id, 'Sale invoice ' || h.invoice_no, h.created_by)
        RETURNING id INTO v_entry;

        SELECT count(*), COALESCE(sum(amount), 0) INTO v_pay_rows, v_pay_total
          FROM sales.payments WHERE company_id = h.company_id AND invoice_id = h.id;
        IF v_pay_rows = 0 THEN
            -- no payment rows (an invoice written by another route): the cash side is the head-office cash account, as before
            v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'CASH'), NULL, v_cash, 0, 'Cash received');
        ELSE
            IF v_pay_total <> v_cash THEN
                RAISE EXCEPTION 'invoice % payments add up to % but % was received (paid % less change %)',
                    h.invoice_no, v_pay_total, v_cash, h.paid_amount, h.change_amount USING ERRCODE = 'check_violation';
            END IF;
            FOR v_pay IN SELECT method, payment_account_id, amount FROM sales.payments
                          WHERE company_id = h.company_id AND invoice_id = h.id ORDER BY received_at, id
            LOOP
                v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, v_pay.payment_account_id, NULL, v_pay.amount, 0,
                                      'Received (' || v_pay.method || ')');
            END LOOP;
        END IF;
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'PARTY_CONTROL'), h.customer_party_id, v_credit, 0, 'Customer balance');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'SALES'), NULL, 0, v_amt, 'Sales');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'TAX_PAYABLE'), NULL, 0, v_tx, 'Tax');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'COGS'), NULL, v_k, 0, 'Cost of goods sold');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'STOCK'), NULL, 0, v_k, 'Stock issued');

    ELSIF h.doc_type = 'RETURN' THEN
        IF h.paid_amount <> 0 OR h.change_amount <> 0 THEN
            RAISE EXCEPTION 'invoice % records a cash refund, which is not modelled yet (decision pending)', h.invoice_no
                USING ERRCODE = 'feature_not_supported';
        END IF;
        v_amt := -h.net_amount + h.tax_amount;   -- sales value returned, tax excluded
        v_tx := -h.tax_amount;
        v_k := -h.cost_amount;
        v_credit := -h.net_amount;               -- amount the customer no longer owes
        IF v_amt < 0 OR v_tx < 0 OR v_k < 0 OR v_credit < 0 THEN
            RAISE EXCEPTION 'invoice % return amounts have the wrong sign', h.invoice_no USING ERRCODE = 'check_violation';
        END IF;
        v_customer := h.customer_party_id;

        INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
        VALUES (h.company_id, h.sale_date, h.store_id, 'SALE_RETURN', h.id, 'Return invoice ' || h.invoice_no, h.created_by)
        RETURNING id INTO v_entry;
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'SALES'), NULL, v_amt, 0, 'Sales returned');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'TAX_PAYABLE'), NULL, v_tx, 0, 'Tax reversed');
        IF v_customer IS NOT NULL THEN
            v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'PARTY_CONTROL'), v_customer, 0, v_credit, 'Customer credited');
        ELSE
            v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'SUSPENSE'), NULL, 0, v_credit, 'Return without customer: accountant to clear');
        END IF;
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'STOCK'), NULL, v_k, 0, 'Stock returned');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'COGS'), NULL, 0, v_k, 'Cost of goods reversed');
    END IF;

    UPDATE sales.invoices SET journal_entry_id = v_entry WHERE company_id = h.company_id AND id = h.id;
    RETURN NULL;
END $$;

-- ---------------------------------------------------------------------
-- 4. sales.post_sale_invoice
--    p_lines:    [{"variantId": "...", "quantity": 2, "unitPrice": 1000, "discountType": "PERCENT"|"AMOUNT"|"NONE", "discountValue": 10}]
--                unitPrice is the price per unit before the line discount; discountType AMOUNT is rupees per unit.
--    p_payments: [{"method": "CASH", "paymentAccountId": "...", "amount": 1500, "reference": "..."}]  amount = what was kept (after change)
--    p_change:   cash handed back to the customer (the docx "Cash Return"); the header paid amount is payments + change.
--    Returns the invoice id (an existing one for a repeated client transaction).
-- ---------------------------------------------------------------------
CREATE FUNCTION sales.post_sale_invoice(
    p_company uuid, p_store uuid, p_sale_date date, p_client_transaction uuid,
    p_customer uuid, p_salesman uuid, p_walkin_name text, p_walkin_mobile text, p_device uuid,
    p_description text, p_lines jsonb, p_payments jsonb, p_change core.money,
    p_price_change_permission text, p_discount_permission text,
    p_replaces_invoice uuid DEFAULT NULL, p_replace_reason text DEFAULT NULL,
    p_allow_walkin_credit boolean DEFAULT false)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    c_head_office constant uuid := '00000000-0000-0000-0000-000000000000';
    v_existing      uuid;
    v_channel       uuid;
    v_invoice       uuid := uuidv7();
    v_invoice_no    bigint;
    v_daily_bill    integer;
    v_walkin        uuid;
    v_max_manual    numeric;
    v_calculated    jsonb := '[]'::jsonb;
    v_line_count    int;
    e               record;
    v           record;
    p               record;
    v_qty           numeric;
    v_input_price   bigint;
    v_type          text;
    v_value         numeric;
    v_engine_applies boolean;
    v_expected      bigint;
    v_per_unit      bigint;
    v_charged       bigint;
    v_source        text;
    v_on_hand       numeric;
    v_unit_cost     numeric;
    v_gross         bigint;
    v_net           bigint;
    v_cost          bigint;
    t_gross         bigint := 0;
    t_net           bigint := 0;
    t_cost          bigint := 0;
    t_discount      bigint := 0;
    v_applied       bigint := 0;
    v_cash_applied  bigint := 0;
    v_paid          bigint;
    v_credit        bigint;
    v_pay_count     int := 0;
BEGIN
    PERFORM sales.ensure_permission(p_company, 'SALE_CREATE', 'You are not allowed to create sales.');
    IF p_client_transaction IS NULL THEN
        RAISE EXCEPTION 'a client transaction id is required' USING ERRCODE = 'check_violation';
    END IF;

    -- two simultaneous requests with the same client transaction id are serialised here; the second one finds the first one's invoice
    PERFORM pg_advisory_xact_lock(hashtextextended('sales.client_transaction:' || p_client_transaction::text, 0));
    SELECT id INTO v_existing FROM sales.invoices WHERE company_id = p_company AND device_sale_uuid = p_client_transaction;
    IF v_existing IS NOT NULL THEN
        RETURN v_existing;
    END IF;

    IF p_store IS NULL OR p_store = c_head_office THEN
        RAISE EXCEPTION 'choose the store that makes the sale' USING ERRCODE = 'check_violation';
    END IF;
    IF p_sale_date IS NULL THEN
        RAISE EXCEPTION 'the sale needs a date' USING ERRCODE = 'check_violation';
    END IF;
    IF p_lines IS NULL OR jsonb_typeof(p_lines) <> 'array' THEN
        RAISE EXCEPTION 'the sale has no items' USING ERRCODE = 'check_violation';
    END IF;
    v_line_count := jsonb_array_length(p_lines);
    IF v_line_count < 1 THEN
        RAISE EXCEPTION 'add at least one item to the sale' USING ERRCODE = 'check_violation';
    END IF;
    IF v_line_count > 200 THEN
        RAISE EXCEPTION 'a sale can have at most 200 lines' USING ERRCODE = 'check_violation';
    END IF;

    SELECT id INTO v_channel FROM integ.channels WHERE company_id = p_company AND code = 'POS' AND is_active;
    IF v_channel IS NULL THEN
        RAISE EXCEPTION 'the POS sales channel of this company is not active' USING ERRCODE = 'check_violation';
    END IF;
    SELECT id INTO v_walkin FROM party.parties WHERE company_id = p_company AND code = 'WALKIN' AND NOT is_deleted;

    IF p_customer IS NOT NULL THEN
        PERFORM 1 FROM party.parties WHERE company_id = p_company AND id = p_customer AND is_customer AND NOT is_deleted AND status = 'ACTIVE';
        IF NOT FOUND THEN
            RAISE EXCEPTION 'the customer was not found or is not an active customer' USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF p_salesman IS NOT NULL THEN
        PERFORM 1 FROM party.parties WHERE company_id = p_company AND id = p_salesman AND is_staff AND NOT is_deleted AND status = 'ACTIVE';
        IF NOT FOUND THEN
            RAISE EXCEPTION 'the salesman was not found or is not an active staff member' USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF p_device IS NOT NULL THEN
        PERFORM 1 FROM core.devices WHERE company_id = p_company AND id = p_device AND store_id = p_store AND is_active;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'the POS terminal does not belong to this store or is not active' USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF COALESCE(p_change, 0) < 0 THEN
        RAISE EXCEPTION 'the cash returned cannot be negative' USING ERRCODE = 'check_violation';
    END IF;

    -- "void and re-enter": the old invoice is voided first (stock back, journal reversed), then the corrected one is posted, all in this transaction
    IF p_replaces_invoice IS NOT NULL THEN
        PERFORM 1 FROM sales.invoices WHERE company_id = p_company AND id = p_replaces_invoice AND doc_type = 'SALE';
        IF NOT FOUND THEN
            RAISE EXCEPTION 'the invoice to replace was not found or is not a sale' USING ERRCODE = 'check_violation';
        END IF;
        PERFORM sales.ensure_permission(p_company, 'INVOICE.VIEW_INVOICES.EDIT', 'You are not allowed to edit invoices.');
        PERFORM sales.void_invoice(p_company, p_replaces_invoice, p_replace_reason);
    END IF;

    SELECT (setting_values ->> 'maximumManualDiscountPercentage')::numeric INTO v_max_manual
      FROM core.company_settings WHERE company_id = p_company AND section_code = 'PROMOTION_POLICY';

    -- pass 1: price every line and take its stock out. Stock rows are locked in ascending variant order, so two sales cannot deadlock.
    FOR e IN
        SELECT x.ord::int AS line_no, x.elem AS elem
          FROM jsonb_array_elements(p_lines) WITH ORDINALITY AS x(elem, ord)
         ORDER BY (x.elem ->> 'variantId')::uuid, x.ord
    LOOP
        v_qty := (e.elem ->> 'quantity')::numeric;
        IF v_qty IS NULL OR v_qty <= 0 OR v_qty <> round(v_qty, 3) OR v_qty > 999999 THEN
            RAISE EXCEPTION 'line %: the quantity must be above zero with at most 3 decimals', e.line_no USING ERRCODE = 'check_violation';
        END IF;
        v_input_price := (e.elem ->> 'unitPrice')::bigint;
        IF v_input_price IS NULL OR v_input_price < 0 THEN
            RAISE EXCEPTION 'line %: the price cannot be negative', e.line_no USING ERRCODE = 'check_violation';
        END IF;

        SELECT va.id AS variant_id, va.product_id, va.color_id, va.size_group_id, va.size_code, va.barcode,
               pr.name AS product_name, pr.category_id, pr.department_id, pr.brand_id, pr.season_id, pr.gender_id, pr.product_type_id, pr.tax_rate,
               COALESCE(o.sale_price, vp.sale_price) AS shelf_price,
               ep.sale_price AS engine_price, ep.list_price AS engine_list_price, ep.rule_id AS engine_rule_id,
               COALESCE(ep.is_conflict, false) AS engine_conflict, ep.conflict_group_id AS engine_conflict_group, ep.valid_until AS engine_valid_until
          INTO v
          FROM catalog.variants va
          JOIN catalog.products pr ON pr.company_id = va.company_id AND pr.id = va.product_id
          JOIN catalog.variant_prices vp ON vp.company_id = va.company_id AND vp.variant_id = va.id
          LEFT JOIN catalog.store_price_overrides o ON o.company_id = va.company_id AND o.store_id = p_store AND o.variant_id = va.id
          LEFT JOIN promo.effective_prices ep ON ep.company_id = va.company_id AND ep.channel_id = v_channel AND ep.store_id = p_store AND ep.variant_id = va.id
         WHERE va.company_id = p_company AND va.id = (e.elem ->> 'variantId')::uuid
           AND NOT va.is_deleted AND va.status = 'ACTIVE' AND NOT pr.is_deleted AND pr.status = 'ACTIVE';
        IF NOT FOUND THEN
            RAISE EXCEPTION 'line %: the item was not found or is not for sale', e.line_no USING ERRCODE = 'check_violation';
        END IF;
        IF v.tax_rate > 0 THEN
            RAISE EXCEPTION 'tax calculation is not configured: % has a tax rate of % percent, and the way tax is calculated has not been decided yet', v.product_name, trim_scale(v.tax_rate)
                USING ERRCODE = 'check_violation';
        END IF;

        -- the price the engine (promotions) says this item sells for, when it applies right now
        v_engine_applies := v.engine_rule_id IS NOT NULL AND NOT v.engine_conflict AND v.engine_list_price = v.shelf_price
                            AND (v.engine_valid_until IS NULL OR v.engine_valid_until > now());
        v_expected := CASE WHEN v_engine_applies THEN v.engine_price ELSE v.shelf_price END;

        IF v_input_price <> v_expected THEN
            PERFORM sales.ensure_permission(p_company, p_price_change_permission, 'You are not allowed to change the selling price.');
            PERFORM sales.ensure_permission(p_company, 'PRICE_CHANGE', 'You are not allowed to change the selling price.');
        END IF;

        v_type := COALESCE(e.elem ->> 'discountType', 'NONE');
        v_value := (e.elem ->> 'discountValue')::numeric;
        IF v_type = 'PERCENT' THEN
            IF v_value IS NULL OR v_value <= 0 OR v_value > 100 OR v_value <> round(v_value, 2) THEN
                RAISE EXCEPTION 'line %: the discount must be a percentage above 0 and up to 100 with at most 2 decimals', e.line_no USING ERRCODE = 'check_violation';
            END IF;
            v_per_unit := core.round_money(p_company, v_input_price * v_value / 100.0);
        ELSIF v_type = 'AMOUNT' THEN
            IF v_value IS NULL OR v_value <= 0 OR v_value <> trunc(v_value) THEN
                RAISE EXCEPTION 'line %: the discount must be a whole number of rupees above zero', e.line_no USING ERRCODE = 'check_violation';
            END IF;
            v_per_unit := v_value::bigint;
        ELSIF v_type = 'NONE' THEN
            v_per_unit := 0;
        ELSE
            RAISE EXCEPTION 'line %: unknown discount type %', e.line_no, v_type USING ERRCODE = 'check_violation';
        END IF;
        IF v_per_unit > v_input_price THEN
            RAISE EXCEPTION 'line %: the discount is more than the price', e.line_no USING ERRCODE = 'check_violation';
        END IF;
        IF v_per_unit > 0 THEN
            PERFORM sales.ensure_permission(p_company, p_discount_permission, 'You are not allowed to give a discount.');
            PERFORM sales.ensure_permission(p_company, 'DISCOUNT_MANUAL', 'You are not allowed to give a discount.');
            IF v_max_manual IS NOT NULL AND v_per_unit * 100.0 > v_input_price * v_max_manual THEN
                RAISE EXCEPTION 'line %: a manual discount may be at most % percent (company setting)', e.line_no, trim_scale(v_max_manual) USING ERRCODE = 'check_violation';
            END IF;
        END IF;

        v_charged := v_input_price - v_per_unit;
        IF v_charged > v.shelf_price THEN
            RAISE EXCEPTION 'line %: the price cannot be above the shelf price of %', e.line_no, v.shelf_price USING ERRCODE = 'check_violation';
        END IF;
        v_source := CASE WHEN v_charged = v.shelf_price THEN 'NONE'
                         WHEN v_engine_applies AND v_charged = v.engine_price THEN 'RULE'
                         ELSE 'MANUAL' END;

        SELECT COALESCE((SELECT qty_on_hand FROM inv.stock_balances
                          WHERE company_id = p_company AND store_id = p_store AND variant_id = v.variant_id), 0) INTO v_on_hand;
        IF v_on_hand < v_qty THEN
            RAISE EXCEPTION 'Not enough stock of % (size %): % on hand, % requested.', v.product_name, v.size_code, trim_scale(v_on_hand), trim_scale(v_qty)
                USING ERRCODE = 'check_violation';
        END IF;
        v_unit_cost := inv.post_stock_move(p_company, p_store, v.variant_id, -v_qty, 'SALE', v_invoice, NULL, p_sale_date);

        v_gross := core.round_money(p_company, v_qty * v.shelf_price);
        v_net := core.round_money(p_company, v_qty * v_charged);
        v_cost := core.round_money(p_company, v_qty * v_unit_cost);
        t_gross := t_gross + v_gross;
        t_net := t_net + v_net;
        t_cost := t_cost + v_cost;

        v_calculated := v_calculated || jsonb_build_object(
            'line_no', e.line_no, 'id', uuidv7(), 'variant_id', v.variant_id, 'product_id', v.product_id, 'category_id', v.category_id,
            'department_id', v.department_id, 'brand_id', v.brand_id, 'season_id', v.season_id, 'gender_id', v.gender_id, 'color_id', v.color_id,
            'size_group_id', v.size_group_id, 'product_type_id', v.product_type_id, 'qty', v_qty, 'list_price', v.shelf_price,
            'unit_price', v_charged, 'discount_source', v_source,
            'rule_id', CASE WHEN v_source = 'RULE' THEN v.engine_rule_id END,
            'conflict_group_id', v.engine_conflict_group,
            'discount_amount', v_gross - v_net, 'net_amount', v_net, 'unit_cost', v_unit_cost, 'cost_amount', v_cost);
    END LOOP;
    t_discount := t_gross - t_net;

    -- payments: what was kept in each account; the change is handed back out of the cash
    IF p_payments IS NOT NULL AND jsonb_typeof(p_payments) = 'array' THEN
        FOR p IN SELECT x.elem AS elem FROM jsonb_array_elements(p_payments) AS x(elem) LOOP
            v_pay_count := v_pay_count + 1;
            IF v_pay_count > 6 THEN
                RAISE EXCEPTION 'a sale can be paid with at most 6 payments' USING ERRCODE = 'check_violation';
            END IF;
            IF (p.elem ->> 'method') NOT IN ('CASH', 'CARD', 'BANK_TRANSFER', 'CHEQUE', 'ONLINE') THEN
                RAISE EXCEPTION 'unknown payment method %', p.elem ->> 'method' USING ERRCODE = 'check_violation';
            END IF;
            IF (p.elem ->> 'amount') IS NULL OR (p.elem ->> 'amount')::bigint <= 0 THEN
                RAISE EXCEPTION 'every payment needs an amount above zero' USING ERRCODE = 'check_violation';
            END IF;
            PERFORM 1 FROM acct.accounts a
             WHERE a.company_id = p_company AND a.id = (p.elem ->> 'paymentAccountId')::uuid AND a.is_active AND a.is_postable AND NOT a.is_party_control
               AND (a.system_role IN ('CASH', 'BANK') OR a.group_code IN ('101', '102'));
            IF NOT FOUND THEN
                RAISE EXCEPTION 'the payment account is not an active cash or bank account' USING ERRCODE = 'check_violation';
            END IF;
            v_applied := v_applied + (p.elem ->> 'amount')::bigint;
            IF p.elem ->> 'method' = 'CASH' THEN v_cash_applied := v_cash_applied + (p.elem ->> 'amount')::bigint; END IF;
        END LOOP;
    END IF;
    IF COALESCE(p_change, 0) > 0 AND v_cash_applied = 0 THEN
        RAISE EXCEPTION 'cash can only be returned when part of the payment is cash' USING ERRCODE = 'check_violation';
    END IF;
    v_paid := v_applied + COALESCE(p_change, 0);
    v_credit := t_net - v_applied;
    IF v_credit < 0 THEN
        RAISE EXCEPTION 'the payment (%) is more than the bill (%): enter the cash received and let the change be returned', v_applied, t_net
            USING ERRCODE = 'check_violation';
    END IF;
    IF v_credit > 0 AND (p_customer IS NULL OR (p_customer = v_walkin AND NOT p_allow_walkin_credit)) THEN
        RAISE EXCEPTION 'Part of the bill (%) is unpaid. Choose a customer to put it on credit, or take the full payment.', v_credit
            USING ERRCODE = 'check_violation';
    END IF;

    v_invoice_no := core.next_number(p_company, p_store, 'SALE_INVOICE');
    v_daily_bill := core.next_number(p_company, p_store, 'SALE_DAILY_BILL', p_sale_date)::int;

    INSERT INTO sales.invoices (company_id, id, store_id, channel_id, doc_type, invoice_no, sale_date, daily_bill_no, device_id, device_sale_uuid,
                                occurred_at, customer_party_id, salesman_party_id, walkin_name, walkin_mobile,
                                gross_amount, line_discount_amount, bill_discount_amount, taxable_amount, tax_amount, net_amount,
                                paid_amount, change_amount, credit_amount, cost_amount, created_by)
    VALUES (p_company, v_invoice, p_store, v_channel, 'SALE', v_invoice_no, p_sale_date, v_daily_bill, p_device, p_client_transaction,
            now(), p_customer, p_salesman, NULLIF(btrim(p_walkin_name), ''), NULLIF(btrim(p_walkin_mobile), ''),
            t_gross, t_discount, 0, t_net, 0, t_net,
            v_paid, COALESCE(p_change, 0), v_credit, t_cost, core.current_user_id());

    INSERT INTO sales.invoice_lines (company_id, id, invoice_id, line_no, store_id, channel_id, sale_date, doc_type, variant_id, product_id,
                                     category_id, department_id, brand_id, season_id, gender_id, color_id, size_group_id, product_type_id,
                                     salesman_party_id, qty, list_price, unit_price, discount_source, rule_id, conflict_group_id,
                                     discount_amount, tax_rate, taxable_amount, tax_amount, net_amount, unit_cost, cost_amount)
    SELECT p_company, r.id, v_invoice, r.line_no, p_store, v_channel, p_sale_date, 'SALE', r.variant_id, r.product_id,
           r.category_id, r.department_id, r.brand_id, r.season_id, r.gender_id, r.color_id, r.size_group_id, r.product_type_id,
           p_salesman, r.qty, r.list_price, r.unit_price, r.discount_source, r.rule_id, r.conflict_group_id,
           r.discount_amount, 0, r.net_amount, 0, r.net_amount, r.unit_cost, r.cost_amount
      FROM jsonb_to_recordset(v_calculated) AS r(line_no int, id uuid, variant_id uuid, product_id uuid, category_id uuid, department_id uuid,
                                                  brand_id uuid, season_id uuid, gender_id uuid, color_id uuid, size_group_id uuid, product_type_id uuid,
                                                  qty numeric, list_price bigint, unit_price bigint, discount_source text, rule_id uuid,
                                                  conflict_group_id uuid, discount_amount bigint, net_amount bigint, unit_cost numeric, cost_amount bigint)
     ORDER BY r.line_no;

    IF jsonb_typeof(p_payments) = 'array' THEN
        INSERT INTO sales.payments (company_id, invoice_id, store_id, sale_date, method, payment_account_id, amount, reference)
        SELECT p_company, v_invoice, p_store, p_sale_date, x.elem ->> 'method', (x.elem ->> 'paymentAccountId')::uuid,
               (x.elem ->> 'amount')::bigint, NULLIF(btrim(x.elem ->> 'reference'), '')
          FROM jsonb_array_elements(p_payments) AS x(elem);
    END IF;

    IF NULLIF(btrim(p_description), '') IS NOT NULL OR p_replaces_invoice IS NOT NULL THEN
        INSERT INTO sales.invoice_extras (company_id, invoice_id, store_id, description, replaces_invoice_id)
        VALUES (p_company, v_invoice, p_store, NULLIF(btrim(p_description), ''), p_replaces_invoice);
    END IF;

    RETURN v_invoice;
END $$;

-- ---------------------------------------------------------------------
-- 5. sales.post_return_invoice
--    p_lines: [{"originalLineId": "...", "quantity": 1, "reason": "too small", "itemCondition": "SELLABLE"|"DAMAGED"}]
--    The returned goods go back into the stock of the returning store at the cost they left it with. The customer's account is credited
--    (or the suspense account when there is no customer): paying cash back is a business decision the baseline does not model.
-- ---------------------------------------------------------------------
CREATE FUNCTION sales.post_return_invoice(
    p_company uuid, p_store uuid, p_sale_date date, p_client_transaction uuid, p_customer uuid,
    p_description text, p_lines jsonb, p_device uuid DEFAULT NULL, p_keep_walkin_customer boolean DEFAULT false)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    c_head_office constant uuid := '00000000-0000-0000-0000-000000000000';
    v_existing   uuid;
    v_channel    uuid;
    v_invoice    uuid := uuidv7();
    v_invoice_no bigint;
    v_walkin     uuid;
    v_customer   uuid;
    v_first_original uuid;
    v_first_customer uuid;
    v_first_line_no  int;
    v_calculated jsonb := '[]'::jsonb;
    v_line_count int;
    e            record;
    o            record;
    v_qty        numeric;
    v_gross      bigint;
    v_net        bigint;
    v_cost       bigint;
    t_gross      bigint := 0;
    t_net        bigint := 0;
    t_cost       bigint := 0;
    v_condition  text;
BEGIN
    PERFORM sales.ensure_permission(p_company, 'SALE_RETURN', 'You are not allowed to process returns.');
    IF p_client_transaction IS NULL THEN
        RAISE EXCEPTION 'a client transaction id is required' USING ERRCODE = 'check_violation';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('sales.client_transaction:' || p_client_transaction::text, 0));
    SELECT id INTO v_existing FROM sales.invoices WHERE company_id = p_company AND device_sale_uuid = p_client_transaction;
    IF v_existing IS NOT NULL THEN
        RETURN v_existing;
    END IF;

    IF p_store IS NULL OR p_store = c_head_office THEN
        RAISE EXCEPTION 'choose the store that takes the goods back' USING ERRCODE = 'check_violation';
    END IF;
    IF p_sale_date IS NULL THEN
        RAISE EXCEPTION 'the return needs a date' USING ERRCODE = 'check_violation';
    END IF;
    IF p_lines IS NULL OR jsonb_typeof(p_lines) <> 'array' OR jsonb_array_length(p_lines) < 1 THEN
        RAISE EXCEPTION 'add at least one returned item' USING ERRCODE = 'check_violation';
    END IF;
    v_line_count := jsonb_array_length(p_lines);
    IF v_line_count > 200 THEN
        RAISE EXCEPTION 'a return can have at most 200 lines' USING ERRCODE = 'check_violation';
    END IF;
    SELECT id INTO v_channel FROM integ.channels WHERE company_id = p_company AND code = 'POS' AND is_active;
    IF v_channel IS NULL THEN
        RAISE EXCEPTION 'the POS sales channel of this company is not active' USING ERRCODE = 'check_violation';
    END IF;
    SELECT id INTO v_walkin FROM party.parties WHERE company_id = p_company AND code = 'WALKIN' AND NOT is_deleted;
    IF p_device IS NOT NULL THEN
        PERFORM 1 FROM core.devices WHERE company_id = p_company AND id = p_device AND store_id = p_store AND is_active;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'the POS terminal does not belong to this store or is not active' USING ERRCODE = 'check_violation';
        END IF;
    END IF;

    -- the same original line named twice in one request: the two quantities together may not pass what is still returnable
    -- (the trigger of the line table only sees one row at a time, so this is checked here for the whole request)
    PERFORM 1
       FROM (SELECT (x.elem ->> 'originalLineId')::uuid AS original_line_id, sum((x.elem ->> 'quantity')::numeric) AS requested
               FROM jsonb_array_elements(p_lines) AS x(elem) GROUP BY 1) q
       JOIN sales.invoice_lines sl ON sl.company_id = p_company AND sl.id = q.original_line_id
      WHERE q.requested > sl.qty - COALESCE((SELECT sum(-rl.qty) FROM sales.invoice_lines rl
                                               JOIN sales.invoices ri ON ri.company_id = rl.company_id AND ri.id = rl.invoice_id
                                              WHERE rl.company_id = sl.company_id AND rl.original_line_id = sl.id
                                                AND rl.original_line_sale_date = sl.sale_date AND ri.status = 'POSTED'), 0);
    IF FOUND THEN
        RAISE EXCEPTION 'the returned quantity of an item is more than was sold and not yet returned' USING ERRCODE = 'check_violation';
    END IF;

    -- pass 1: check every returned line against its original sale line and put the goods back, in ascending variant order
    FOR e IN
        SELECT x.ord::int AS line_no, x.elem AS elem, oi.variant_id AS ordering_variant
          FROM jsonb_array_elements(p_lines) WITH ORDINALITY AS x(elem, ord)
          LEFT JOIN sales.invoice_lines oi ON oi.company_id = p_company AND oi.id = (x.elem ->> 'originalLineId')::uuid
         ORDER BY oi.variant_id, x.ord
    LOOP
        SELECT il.*, i.status AS invoice_status, i.store_id AS invoice_store_id, i.doc_type AS invoice_doc_type, i.invoice_no AS original_invoice_no,
               i.customer_party_id AS invoice_customer
          INTO o
          FROM sales.invoice_lines il
          JOIN sales.invoices i ON i.company_id = il.company_id AND i.id = il.invoice_id
         WHERE il.company_id = p_company AND il.id = (e.elem ->> 'originalLineId')::uuid;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'line %: the sale line being returned was not found', e.line_no USING ERRCODE = 'check_violation';
        END IF;
        IF o.invoice_doc_type <> 'SALE' OR o.invoice_status <> 'POSTED' THEN
            RAISE EXCEPTION 'line %: only items of a posted sale invoice can be returned (invoice % is %)', e.line_no, o.original_invoice_no,
                CASE WHEN o.invoice_status = 'VOID' THEN 'void' ELSE 'not a sale' END USING ERRCODE = 'check_violation';
        END IF;
        IF o.invoice_store_id <> p_store THEN
            RAISE EXCEPTION 'line %: invoice % was made in another store; goods are returned to the store that sold them (returns at another store need a business decision)',
                e.line_no, o.original_invoice_no USING ERRCODE = 'check_violation';
        END IF;
        v_qty := (e.elem ->> 'quantity')::numeric;
        IF v_qty IS NULL OR v_qty <= 0 OR v_qty <> round(v_qty, 3) THEN
            RAISE EXCEPTION 'line %: the returned quantity must be above zero with at most 3 decimals', e.line_no USING ERRCODE = 'check_violation';
        END IF;
        v_condition := COALESCE(e.elem ->> 'itemCondition', 'SELLABLE');
        IF v_condition NOT IN ('SELLABLE', 'DAMAGED') THEN
            RAISE EXCEPTION 'line %: the item condition must be SELLABLE or DAMAGED', e.line_no USING ERRCODE = 'check_violation';
        END IF;
        IF length(COALESCE(e.elem ->> 'reason', '')) > 200 THEN
            RAISE EXCEPTION 'line %: the return reason is too long (at most 200 characters)', e.line_no USING ERRCODE = 'check_violation';
        END IF;

        -- stock back in at the cost the goods left with (average cost is recalculated by the stock function)
        PERFORM inv.post_stock_move(p_company, p_store, o.variant_id, v_qty, 'SALE_RETURN', v_invoice, o.unit_cost, p_sale_date);

        v_gross := -core.round_money(p_company, v_qty * o.list_price);
        v_net := -core.round_money(p_company, v_qty * o.unit_price);
        v_cost := -core.round_money(p_company, o.cost_amount * v_qty / o.qty);
        t_gross := t_gross + v_gross;
        t_net := t_net + v_net;
        t_cost := t_cost + v_cost;
        -- the header names the original invoice of the first line the cashier entered; every line keeps its own original line
        IF v_first_original IS NULL OR e.line_no < v_first_line_no THEN
            v_first_original := o.invoice_id;
            v_first_customer := o.invoice_customer;
            v_first_line_no := e.line_no;
        END IF;

        v_calculated := v_calculated || jsonb_build_object(
            'line_no', e.line_no, 'id', uuidv7(), 'variant_id', o.variant_id, 'product_id', o.product_id, 'category_id', o.category_id,
            'department_id', o.department_id, 'brand_id', o.brand_id, 'season_id', o.season_id, 'gender_id', o.gender_id, 'color_id', o.color_id,
            'size_group_id', o.size_group_id, 'product_type_id', o.product_type_id, 'salesman_party_id', o.salesman_party_id,
            'price_band_id', o.price_band_id, 'qty', -v_qty, 'list_price', o.list_price, 'unit_price', o.unit_price,
            'discount_source', o.discount_source, 'rule_id', o.rule_id, 'conflict_group_id', o.conflict_group_id, 'bundle_id', o.bundle_id,
            'discount_amount', v_gross - v_net, 'net_amount', v_net, 'unit_cost', o.unit_cost, 'cost_amount', v_cost,
            'original_line_id', o.id, 'original_line_sale_date', o.sale_date,
            'reason', NULLIF(btrim(e.elem ->> 'reason'), ''), 'item_condition', v_condition);
    END LOOP;

    -- the credit goes to the chosen customer, else to the customer of the sale; "Cash Sale" (the walk-in) is no account to credit
    v_customer := COALESCE(p_customer, v_first_customer);
    IF v_customer IS NOT NULL THEN
        PERFORM 1 FROM party.parties WHERE company_id = p_company AND id = v_customer AND is_customer AND NOT is_deleted AND status = 'ACTIVE';
        IF NOT FOUND THEN
            RAISE EXCEPTION 'the customer was not found or is not an active customer' USING ERRCODE = 'check_violation';
        END IF;
        IF v_customer = v_walkin AND NOT p_keep_walkin_customer THEN
            v_customer := NULL;
        END IF;
    END IF;

    v_invoice_no := core.next_number(p_company, p_store, 'SALE_RETURN_INVOICE');
    INSERT INTO sales.invoices (company_id, id, store_id, channel_id, doc_type, invoice_no, sale_date, device_id, device_sale_uuid, occurred_at,
                                original_invoice_id, customer_party_id, salesman_party_id,
                                gross_amount, line_discount_amount, bill_discount_amount, taxable_amount, tax_amount, net_amount,
                                paid_amount, change_amount, credit_amount, cost_amount, created_by)
    VALUES (p_company, v_invoice, p_store, v_channel, 'RETURN', v_invoice_no, p_sale_date, p_device, p_client_transaction, now(),
            v_first_original, v_customer,
            (SELECT salesman_party_id FROM sales.invoices WHERE company_id = p_company AND id = v_first_original),
            t_gross, t_gross - t_net, 0, t_net, 0, t_net,
            0, 0, 0, t_cost, core.current_user_id());

    INSERT INTO sales.invoice_lines (company_id, id, invoice_id, line_no, store_id, channel_id, sale_date, doc_type, variant_id, product_id,
                                     category_id, department_id, brand_id, season_id, gender_id, color_id, size_group_id, product_type_id,
                                     salesman_party_id, price_band_id, qty, list_price, unit_price, discount_source, rule_id, conflict_group_id, bundle_id,
                                     discount_amount, tax_rate, taxable_amount, tax_amount, net_amount, unit_cost, cost_amount,
                                     original_line_id, original_line_sale_date)
    SELECT p_company, r.id, v_invoice, r.line_no, p_store, v_channel, p_sale_date, 'RETURN', r.variant_id, r.product_id,
           r.category_id, r.department_id, r.brand_id, r.season_id, r.gender_id, r.color_id, r.size_group_id, r.product_type_id,
           r.salesman_party_id, r.price_band_id, r.qty, r.list_price, r.unit_price, r.discount_source, r.rule_id, r.conflict_group_id, r.bundle_id,
           r.discount_amount, 0, r.net_amount, 0, r.net_amount, r.unit_cost, r.cost_amount,
           r.original_line_id, r.original_line_sale_date
      FROM jsonb_to_recordset(v_calculated) AS r(line_no int, id uuid, variant_id uuid, product_id uuid, category_id uuid, department_id uuid,
                                                  brand_id uuid, season_id uuid, gender_id uuid, color_id uuid, size_group_id uuid, product_type_id uuid,
                                                  salesman_party_id uuid, price_band_id uuid, qty numeric, list_price bigint, unit_price bigint,
                                                  discount_source text, rule_id uuid, conflict_group_id uuid, bundle_id uuid, discount_amount bigint,
                                                  net_amount bigint, unit_cost numeric, cost_amount bigint, original_line_id uuid,
                                                  original_line_sale_date date)
     ORDER BY r.line_no;

    INSERT INTO sales.invoice_line_extras (company_id, line_id, line_sale_date, store_id, return_reason, item_condition)
    SELECT p_company, r.id, p_sale_date, p_store, r.reason, r.item_condition
      FROM jsonb_to_recordset(v_calculated) AS r(id uuid, reason text, item_condition text);

    IF NULLIF(btrim(p_description), '') IS NOT NULL THEN
        INSERT INTO sales.invoice_extras (company_id, invoice_id, store_id, description)
        VALUES (p_company, v_invoice, p_store, NULLIF(btrim(p_description), ''));
    END IF;

    RETURN v_invoice;
END $$;

-- ---------------------------------------------------------------------
-- 6. sales.void_invoice: "delete" of the docx = a void with a reason. Nothing is deleted: the journal entry is reversed (baseline trigger),
--    the commission is reversed (baseline trigger) and the stock goes back through a reversing stock movement (this function).
--    Both halves of an exchange are voided together. Returns the number of invoices voided.
-- ---------------------------------------------------------------------
CREATE FUNCTION sales.void_one_invoice(p_company uuid, p_invoice uuid, p_reason text, p_partner uuid) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE
    h          sales.invoices%ROWTYPE;
    l          record;
    v_restore  numeric;
    v_on_hand  numeric;
BEGIN
    SELECT * INTO h FROM sales.invoices WHERE company_id = p_company AND id = p_invoice FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'the invoice was not found' USING ERRCODE = 'check_violation';
    END IF;
    IF h.status = 'VOID' THEN
        RAISE EXCEPTION 'invoice % is already void', h.invoice_no USING ERRCODE = 'check_violation';
    END IF;

    IF h.doc_type = 'SALE' THEN
        IF EXISTS (SELECT 1
                     FROM sales.invoice_lines rl
                     JOIN sales.invoices r ON r.company_id = rl.company_id AND r.id = rl.invoice_id
                    WHERE rl.company_id = p_company AND rl.doc_type = 'RETURN' AND r.status = 'POSTED' AND r.id IS DISTINCT FROM p_partner
                      AND rl.original_line_id IN (SELECT id FROM sales.invoice_lines WHERE company_id = p_company AND invoice_id = p_invoice)) THEN
            RAISE EXCEPTION 'invoice % has returns: void the returns first', h.invoice_no USING ERRCODE = 'check_violation';
        END IF;
        IF EXISTS (SELECT 1 FROM acct.voucher_allocations va
                     JOIN acct.vouchers v ON v.company_id = va.company_id AND v.id = va.voucher_id AND v.status = 'POSTED'
                    WHERE va.company_id = p_company AND va.sale_invoice_id = p_invoice) THEN
            RAISE EXCEPTION 'invoice % has payments settled against it: void those receipt vouchers first', h.invoice_no USING ERRCODE = 'check_violation';
        END IF;
    END IF;

    FOR l IN SELECT il.id, il.sale_date, il.variant_id, il.qty, il.unit_cost
               FROM sales.invoice_lines il
              WHERE il.company_id = p_company AND il.invoice_id = p_invoice
              ORDER BY il.variant_id, il.line_no
    LOOP
        IF h.doc_type = 'SALE' THEN
            -- a sale taken out through the offline route may have posted less than it sold: put back only what left
            v_restore := COALESCE((SELECT ssp.posted_qty FROM inv.sale_stock_posts ssp
                                    WHERE ssp.company_id = p_company AND ssp.invoice_line_id = l.id AND ssp.line_sale_date = l.sale_date), l.qty);
            IF v_restore > 0 THEN
                PERFORM inv.post_stock_move(p_company, h.store_id, l.variant_id, v_restore, 'SALE_VOID', p_invoice, l.unit_cost, CURRENT_DATE);
            END IF;
        ELSE
            SELECT COALESCE((SELECT qty_on_hand FROM inv.stock_balances
                              WHERE company_id = p_company AND store_id = h.store_id AND variant_id = l.variant_id), 0) INTO v_on_hand;
            IF v_on_hand < -l.qty THEN
                RAISE EXCEPTION 'return % cannot be voided: the returned goods are no longer in stock (% on hand, % needed)',
                    h.invoice_no, trim_scale(v_on_hand), trim_scale(-l.qty) USING ERRCODE = 'check_violation';
            END IF;
            PERFORM inv.post_stock_move(p_company, h.store_id, l.variant_id, l.qty, 'SALE_VOID', p_invoice, NULL, CURRENT_DATE);
        END IF;
    END LOOP;

    UPDATE sales.invoices
       SET status = 'VOID', void_reason = btrim(p_reason), voided_by = core.current_user_id(), voided_at = now()
     WHERE company_id = p_company AND id = p_invoice;
END $$;

CREATE FUNCTION sales.void_invoice(p_company uuid, p_invoice uuid, p_reason text) RETURNS integer
LANGUAGE plpgsql AS $$
DECLARE
    h          sales.invoices%ROWTYPE;
    v_partner  uuid;
    v_sale     uuid;
    v_return   uuid;
BEGIN
    PERFORM sales.ensure_permission(p_company, 'INVOICE.VIEW_INVOICES.DELETE', 'You are not allowed to void invoices.');
    IF btrim(COALESCE(p_reason, '')) = '' THEN
        RAISE EXCEPTION 'give the reason for voiding the invoice' USING ERRCODE = 'check_violation';
    END IF;
    IF length(p_reason) > 200 THEN
        RAISE EXCEPTION 'the reason is too long (at most 200 characters)' USING ERRCODE = 'check_violation';
    END IF;
    SELECT * INTO h FROM sales.invoices WHERE company_id = p_company AND id = p_invoice;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'the invoice was not found' USING ERRCODE = 'check_violation';
    END IF;

    -- the other half of an exchange is voided with it
    SELECT r.id INTO v_partner FROM sales.invoices r
     WHERE r.company_id = p_company AND r.status = 'POSTED'
       AND r.id = h.related_invoice_id;
    IF v_partner IS NULL THEN
        SELECT r.id INTO v_partner FROM sales.invoices r
         WHERE r.company_id = p_company AND r.status = 'POSTED' AND r.related_invoice_id = p_invoice;
    END IF;

    IF v_partner IS NULL THEN
        PERFORM sales.void_one_invoice(p_company, p_invoice, p_reason, NULL);
        RETURN 1;
    END IF;

    -- the sale half first (its goods come back into stock), then the return half (its goods leave the stock again)
    IF h.doc_type = 'SALE' THEN v_sale := p_invoice; v_return := v_partner; ELSE v_sale := v_partner; v_return := p_invoice; END IF;
    PERFORM sales.void_one_invoice(p_company, v_sale, p_reason, v_return);
    PERFORM sales.void_one_invoice(p_company, v_return, p_reason, v_sale);
    RETURN 2;
END $$;

-- ---------------------------------------------------------------------
-- 7. sales.post_exchange: a return invoice and a sale invoice, linked both ways, in one transaction.
--    With a real customer the difference may be paid in part (the rest, or a surplus, stays on the customer's account).
--    Without a customer ("Cash Sale") the two documents are netted on the walk-in account, which is only clean when the new items
--    cost at least as much as the returned ones and the difference is paid in full: anything else is refused.
-- ---------------------------------------------------------------------
CREATE FUNCTION sales.post_exchange(
    p_company uuid, p_store uuid, p_date date, p_client_transaction uuid,
    p_customer uuid, p_salesman uuid, p_walkin_name text, p_walkin_mobile text, p_device uuid, p_description text,
    p_return_lines jsonb, p_sale_lines jsonb, p_payments jsonb, p_change core.money)
RETURNS TABLE (return_invoice_id uuid, sale_invoice_id uuid) LANGUAGE plpgsql AS $$
DECLARE
    v_walkin   uuid;
    v_customer uuid;
    v_return   uuid;
    v_sale     uuid;
    v_return_net bigint;
    v_sale_credit bigint;
BEGIN
    SELECT id INTO v_walkin FROM party.parties WHERE company_id = p_company AND code = 'WALKIN' AND NOT is_deleted;
    v_customer := p_customer;
    IF v_customer IS NULL THEN
        IF v_walkin IS NULL THEN
            RAISE EXCEPTION 'the Cash Sale customer of this company is missing: choose a customer for the exchange' USING ERRCODE = 'check_violation';
        END IF;
        v_customer := v_walkin;
    END IF;

    v_return := sales.post_return_invoice(p_company, p_store, p_date, md5(p_client_transaction::text || ':return')::uuid, v_customer,
                                          p_description, p_return_lines, p_device, p_customer IS NULL);
    v_sale := sales.post_sale_invoice(p_company, p_store, p_date, p_client_transaction, v_customer, p_salesman, p_walkin_name, p_walkin_mobile, p_device,
                                      p_description, p_sale_lines, p_payments, p_change,
                                      'INVOICE.EXCHANGE_INVOICE.CHANGE_SELLING_PRICE', 'INVOICE.EXCHANGE_INVOICE.GIVE_DISCOUNT',
                                      NULL, NULL, p_customer IS NULL);

    UPDATE sales.invoices SET related_invoice_id = v_sale   WHERE company_id = p_company AND id = v_return AND related_invoice_id IS NULL;
    UPDATE sales.invoices SET related_invoice_id = v_return WHERE company_id = p_company AND id = v_sale   AND related_invoice_id IS NULL;

    IF p_customer IS NULL THEN
        SELECT -net_amount INTO v_return_net FROM sales.invoices WHERE company_id = p_company AND id = v_return;
        SELECT credit_amount INTO v_sale_credit FROM sales.invoices WHERE company_id = p_company AND id = v_sale;
        IF v_sale_credit <> v_return_net THEN
            RAISE EXCEPTION 'With the Cash Sale customer the difference must be paid in full: the new items must cost at least as much as the returned items (%) and the payment must equal the difference. Choose a customer to keep a balance.', v_return_net
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;

    RETURN QUERY SELECT v_return, v_sale;
END $$;

-- ---------------------------------------------------------------------
-- 8. Privileges (the baseline granted function execution only on other schemas, and new functions are not open to PUBLIC here)
-- ---------------------------------------------------------------------
REVOKE ALL ON FUNCTION sales.ensure_permission(uuid, text, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION sales.post_sale_invoice(uuid, uuid, date, uuid, uuid, uuid, text, text, uuid, text, jsonb, jsonb, core.money, text, text, uuid, text, boolean) FROM PUBLIC;
REVOKE ALL ON FUNCTION sales.post_return_invoice(uuid, uuid, date, uuid, uuid, text, jsonb, uuid, boolean) FROM PUBLIC;
REVOKE ALL ON FUNCTION sales.void_one_invoice(uuid, uuid, text, uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION sales.void_invoice(uuid, uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION sales.post_exchange(uuid, uuid, date, uuid, uuid, uuid, text, text, uuid, text, jsonb, jsonb, jsonb, core.money) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sales.ensure_permission(uuid, text, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sales.post_sale_invoice(uuid, uuid, date, uuid, uuid, uuid, text, text, uuid, text, jsonb, jsonb, core.money, text, text, uuid, text, boolean) TO pos_app;
GRANT EXECUTE ON FUNCTION sales.post_return_invoice(uuid, uuid, date, uuid, uuid, text, jsonb, uuid, boolean) TO pos_app;
GRANT EXECUTE ON FUNCTION sales.void_one_invoice(uuid, uuid, text, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION sales.void_invoice(uuid, uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sales.post_exchange(uuid, uuid, date, uuid, uuid, uuid, text, text, uuid, text, jsonb, jsonb, jsonb, core.money) TO pos_app;
