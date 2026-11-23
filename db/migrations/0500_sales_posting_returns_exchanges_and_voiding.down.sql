-- Down of 0500: removes the sales posting functions and the two extras tables, and restores the baseline journal function
-- (the cash side always goes to the head-office CASH role account). Notes and return reasons stored in the extras tables are lost.
DROP FUNCTION sales.post_exchange(uuid, uuid, date, uuid, uuid, uuid, text, text, uuid, text, jsonb, jsonb, jsonb, core.money);
DROP FUNCTION sales.void_invoice(uuid, uuid, text);
DROP FUNCTION sales.void_one_invoice(uuid, uuid, text, uuid);
DROP FUNCTION sales.post_return_invoice(uuid, uuid, date, uuid, uuid, text, jsonb, uuid, boolean);
DROP FUNCTION sales.post_sale_invoice(uuid, uuid, date, uuid, uuid, uuid, text, text, uuid, text, jsonb, jsonb, core.money, text, text, uuid, text, boolean);
DROP FUNCTION sales.ensure_permission(uuid, text, text);

DROP TABLE sales.invoice_line_extras;
DROP TABLE sales.invoice_extras;

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
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'CASH'), NULL, v_cash, 0, 'Cash received');
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

