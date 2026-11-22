-- 0200: master data for accounting and parties (module masterdata-parties, Phase 0 rows ORG-2.1, 2.2, 2.3, 2.18, 3.1).
--   1. acct.account_groups: the "second level" of the chart of accounts (docx 2.2 / 2.3: group code + title under a main type).
--      Until now the group of an account was only a bare text code (acct.accounts.group_code) with no title.
--   2. acct.next_account_code: next account number in a group (docx 2.1: "Account #", read-only, next code in the group).
--   3. party.party_code_counters + party.next_party_code: automatic party code (docx 3.1: "Id ... auto-generated party code").
--   4. acct.post_opening_balance + acct.v_live_opening_balances: opening balances as append-only journal entries.
--      Saving an amount again REVERSES the live opening entry and posts a new one (docx 2.18 says "saving again replaces it";
--      the journal is append-only, so replacing means reverse-and-repost, Part 15). Every opening entry is balanced against the
--      company's OPENING_EQUITY account (acct.accounts.system_role, seeded as 3020 "Opening balance equity").
--      The accounting treatment is a proposal until the business confirms it ([BUSINESS DECISION REQUIRED], Part 15).
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

-- ---------------------------------------------------------------------
-- 1. Account groups
-- ---------------------------------------------------------------------
CREATE TABLE acct.account_groups (
    company_id   uuid NOT NULL REFERENCES platform.companies(id),
    group_code   text NOT NULL CHECK (group_code ~ '^[1-6][0-9]{2}$'),
    main_type    text NOT NULL CHECK (main_type IN ('ASSET','LIABILITY','CAPITAL','INCOME','STOCK','EXPENSE')),
    name         text NOT NULL CHECK (length(btrim(name)) > 0),
    created_at   timestamptz NOT NULL DEFAULT now(),
    created_by   uuid,
    updated_at   timestamptz,
    updated_by   uuid,
    row_ver      bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, group_code),
    -- the first digit of a group code is the main type (docx 2.3: 1 Assets, 2 Liabilities, 3 Capital, 4 Income, 5 Stock, 6 Expenses)
    CONSTRAINT account_groups_digit_matches_type_chk CHECK (
           (left(group_code, 1) = '1' AND main_type = 'ASSET')
        OR (left(group_code, 1) = '2' AND main_type = 'LIABILITY')
        OR (left(group_code, 1) = '3' AND main_type = 'CAPITAL')
        OR (left(group_code, 1) = '4' AND main_type = 'INCOME')
        OR (left(group_code, 1) = '5' AND main_type = 'STOCK')
        OR (left(group_code, 1) = '6' AND main_type = 'EXPENSE'))
);
CREATE UNIQUE INDEX ux_account_groups_name ON acct.account_groups (company_id, main_type, lower(name));
CREATE TRIGGER trg_touch BEFORE UPDATE ON acct.account_groups FOR EACH ROW EXECUTE FUNCTION core.touch_row();
SELECT core.enable_rls('acct.account_groups'::regclass, false);
GRANT SELECT, INSERT, UPDATE, DELETE ON acct.account_groups TO pos_app;

-- A group that still has accounts cannot be deleted (docx 2.3: "Delete only if no accounts use it").
CREATE FUNCTION acct.forbid_deleting_group_in_use() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM acct.accounts a WHERE a.company_id = OLD.company_id AND a.group_code = OLD.group_code) THEN
        RAISE EXCEPTION 'account group % is used by accounts and cannot be deleted', OLD.group_code
            USING ERRCODE = 'foreign_key_violation';
    END IF;
    RETURN OLD;
END $$;
CREATE TRIGGER trg_group_in_use BEFORE DELETE ON acct.account_groups
    FOR EACH ROW EXECUTE FUNCTION acct.forbid_deleting_group_in_use();

-- A system account (cash, bank, party control, sales, stock ...) is looked up by its role by the posting functions: never delete it.
CREATE FUNCTION acct.forbid_deleting_system_account() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.system_role IS NOT NULL THEN
        RAISE EXCEPTION 'account % is a system account (%) and cannot be deleted', OLD.code, OLD.system_role
            USING ERRCODE = 'insufficient_privilege';
    END IF;
    RETURN OLD;
END $$;
CREATE TRIGGER trg_no_system_delete BEFORE DELETE ON acct.accounts
    FOR EACH ROW EXECUTE FUNCTION acct.forbid_deleting_system_account();

-- docx 2.1 "Delete (only if no transactions)": an account that nothing references (journal lines, expense categories ...)
-- can be removed; the foreign keys refuse every other deletion.
GRANT DELETE ON acct.accounts TO pos_app;

-- Titles of the groups the default chart uses (see acct.seed_default_chart and core.on_store_created). The titles are labels
-- that the company may rename; they carry no policy.
CREATE FUNCTION acct.default_group_name(p_group_code text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
    SELECT CASE p_group_code
        WHEN '101' THEN 'Cash'                          WHEN '102' THEN 'Bank'
        WHEN '103' THEN 'Party control'                 WHEN '201' THEN 'Tax payable'
        WHEN '202' THEN 'Salary payable'                WHEN '203' THEN 'Expense payable'
        WHEN '209' THEN 'Suspense'                      WHEN '301' THEN 'Capital'
        WHEN '302' THEN 'Opening balance equity'        WHEN '401' THEN 'Sales'
        WHEN '501' THEN 'Stock'                         WHEN '502' THEN 'Stock on hand'
        WHEN '600' THEN 'Cost of goods sold'            WHEN '601' THEN 'Stock loss'
        WHEN '602' THEN 'Cost of goods sold (store)'    WHEN '603' THEN 'Salary expense'
        WHEN '610' THEN 'Expenses'
        ELSE 'Group ' || p_group_code END
$$;

-- Existing companies: one group row for every group code their accounts already use.
INSERT INTO acct.account_groups (company_id, group_code, main_type, name)
SELECT DISTINCT ON (a.company_id, a.group_code) a.company_id, a.group_code, a.main_type, acct.default_group_name(a.group_code)
  FROM acct.accounts a
 WHERE a.group_code ~ '^[1-6][0-9]{2}$'
 ORDER BY a.company_id, a.group_code, a.code
ON CONFLICT DO NOTHING;

-- New companies: the default chart creates its groups first. This is the baseline function with the group inserts added in
-- front; 0200.down restores the baseline body.
CREATE OR REPLACE FUNCTION acct.seed_default_chart(p_company uuid) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO acct.account_groups (company_id, group_code, main_type, name)
    SELECT p_company, g.group_code, g.main_type, acct.default_group_name(g.group_code)
      FROM (VALUES ('101','ASSET'), ('102','ASSET'), ('103','ASSET'),
                   ('201','LIABILITY'), ('202','LIABILITY'), ('203','LIABILITY'), ('209','LIABILITY'),
                   ('301','CAPITAL'), ('302','CAPITAL'),
                   ('401','INCOME'),
                   ('501','STOCK'), ('502','STOCK'),
                   ('600','EXPENSE'), ('601','EXPENSE'), ('602','EXPENSE'), ('603','EXPENSE'), ('610','EXPENSE'))
           AS g(group_code, main_type);

    INSERT INTO acct.accounts (company_id, code, main_type, group_code, name, store_id, system_role, is_party_control) VALUES
        (p_company, '1010', 'ASSET',     '101', 'Cash in hand (head office)', '00000000-0000-0000-0000-000000000000'::uuid, 'CASH', false),
        (p_company, '1020', 'ASSET',     '102', 'Bank',                       '00000000-0000-0000-0000-000000000000'::uuid, 'BANK', false),
        (p_company, '1030', 'ASSET',     '103', 'Party control (customers, vendors, staff)', '00000000-0000-0000-0000-000000000000'::uuid, 'PARTY_CONTROL', true),
        (p_company, '2010', 'LIABILITY', '201', 'Tax payable',                '00000000-0000-0000-0000-000000000000'::uuid, 'TAX_PAYABLE', false),
        (p_company, '2020', 'LIABILITY', '202', 'Salary payable',             '00000000-0000-0000-0000-000000000000'::uuid, 'SALARY_PAYABLE', false),
        (p_company, '2030', 'LIABILITY', '203', 'Expense payable',            '00000000-0000-0000-0000-000000000000'::uuid, 'EXPENSE_PAYABLE', false),
        (p_company, '3010', 'CAPITAL',   '301', 'Capital / investment',       '00000000-0000-0000-0000-000000000000'::uuid, 'CAPITAL', false),
        (p_company, '3020', 'CAPITAL',   '302', 'Opening balance equity',     '00000000-0000-0000-0000-000000000000'::uuid, 'OPENING_EQUITY', false),
        (p_company, '5010', 'STOCK',     '501', 'Stock in transit',           '00000000-0000-0000-0000-000000000000'::uuid, 'TRANSIT', false),
        (p_company, '6010', 'EXPENSE',   '601', 'Stock loss (company bearer)', '00000000-0000-0000-0000-000000000000'::uuid, 'STOCK_LOSS', false);

    INSERT INTO acct.accounts (company_id, code, main_type, group_code, name, store_id, system_role)
    SELECT p_company, 'EXP-' || c.code, 'EXPENSE', '610', 'Expense: ' || c.name, '00000000-0000-0000-0000-000000000000'::uuid, NULL
      FROM (VALUES ('RENT','Rent'), ('UTILITY','Utility bills'), ('RENOVATION','Renovation'),
                   ('BILTY','Bilty (freight / transport)'), ('MISC','Miscellaneous')) AS c(code, name);

    INSERT INTO acct.accounts (company_id, code, main_type, group_code, name, store_id, system_role) VALUES
        (p_company, '4010', 'INCOME',    '401', 'Sales',                        '00000000-0000-0000-0000-000000000000'::uuid, 'SALES'),
        (p_company, '5020', 'STOCK',     '502', 'Stock on hand',                '00000000-0000-0000-0000-000000000000'::uuid, 'STOCK'),
        (p_company, '6100', 'EXPENSE',   '600', 'Cost of goods sold',           '00000000-0000-0000-0000-000000000000'::uuid, 'COGS'),
        (p_company, '2900', 'LIABILITY', '209', 'Suspense (to be cleared)',     '00000000-0000-0000-0000-000000000000'::uuid, 'SUSPENSE');

    INSERT INTO exp.expense_categories (company_id, code, name, gl_account_id, is_recurring)
    SELECT p_company, c.code, c.name, a.id, c.recurring
      FROM (VALUES ('RENT','Rent', true), ('UTILITY','Utility bills', true), ('RENOVATION','Renovation', false),
                   ('BILTY','Bilty (freight / transport)', false), ('MISC','Miscellaneous', false))
           AS c(code, name, recurring)
      JOIN acct.accounts a ON a.company_id = p_company AND a.code = 'EXP-' || c.code;
END $$;

-- ---------------------------------------------------------------------
-- 2. Next account code: group code + 5-digit serial (docx 2.3: "1-digit main type + 2-digit group + 5-digit serial")
-- ---------------------------------------------------------------------
CREATE FUNCTION acct.next_account_code(p_company uuid, p_group_code text) RETURNS text
LANGUAGE plpgsql STABLE AS $$
DECLARE
    v_next bigint;
BEGIN
    SELECT COALESCE(max(substr(a.code, 4)::bigint), 0) + 1
      INTO v_next
      FROM acct.accounts a
     WHERE a.company_id = p_company AND a.group_code = p_group_code AND a.code ~ ('^' || p_group_code || '[0-9]{5}$');
    IF v_next > 99999 THEN
        RAISE EXCEPTION 'account group % has no free account numbers left', p_group_code USING ERRCODE = 'check_violation';
    END IF;
    RETURN p_group_code || lpad(v_next::text, 5, '0');
END $$;

-- ---------------------------------------------------------------------
-- 3. Party codes: group 103 (party control) + 5-digit serial, like the docx example 10300067
-- ---------------------------------------------------------------------
CREATE TABLE party.party_code_counters (
    company_id  uuid PRIMARY KEY REFERENCES platform.companies(id),
    last_value  bigint NOT NULL DEFAULT 0 CHECK (last_value >= 0)
);
SELECT core.enable_rls('party.party_code_counters'::regclass, false);
GRANT SELECT, INSERT, UPDATE ON party.party_code_counters TO pos_app;

CREATE FUNCTION party.next_party_code(p_company uuid) RETURNS text
LANGUAGE plpgsql AS $$
DECLARE
    v_value     bigint;
    v_candidate text;
BEGIN
    LOOP
        INSERT INTO party.party_code_counters (company_id, last_value) VALUES (p_company, 1)
        ON CONFLICT (company_id) DO UPDATE SET last_value = party.party_code_counters.last_value + 1
        RETURNING last_value INTO v_value;
        IF v_value > 99999 THEN
            RAISE EXCEPTION 'no free party codes left for company %', p_company USING ERRCODE = 'check_violation';
        END IF;
        v_candidate := '103' || lpad(v_value::text, 5, '0');
        -- skip a code that was typed by hand or imported earlier
        EXIT WHEN NOT EXISTS (SELECT 1 FROM party.parties p WHERE p.company_id = p_company AND p.code = v_candidate);
    END LOOP;
    RETURN v_candidate;
END $$;

-- ---------------------------------------------------------------------
-- 4. Opening balances
-- ---------------------------------------------------------------------
-- Posts (or replaces) the opening balance of ONE account or ONE party. Exactly one of p_account / p_party is given.
--   debit or credit > 0 : the opening amount; both 0 : clears the opening balance (the live entry is reversed).
--   Account rows are posted in the account's own store; party rows in p_store (head office when null).
--   Stock and in-transit accounts are refused: opening stock is entered as stock quantities by inventory, so the stock ledger
--   and the stock account never disagree.
-- Returns the id of the live opening entry, or null when the balance was cleared.
CREATE FUNCTION acct.post_opening_balance(p_company uuid, p_account uuid, p_party uuid, p_store uuid,
                                          p_debit core.money, p_credit core.money, p_entry_date date)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    v_head_office  CONSTANT uuid := '00000000-0000-0000-0000-000000000000';
    v_account      acct.accounts%ROWTYPE;
    v_target       uuid;
    v_store        uuid;
    v_source       uuid;
    v_equity       uuid;
    v_live         acct.journal_entries%ROWTYPE;
    v_live_debit   core.money;
    v_live_credit  core.money;
    v_entry        uuid;
    v_line         int := 1;
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'OPENING_BALANCE');

    IF (p_account IS NULL) = (p_party IS NULL) THEN
        RAISE EXCEPTION 'name exactly one of account or party for an opening balance' USING ERRCODE = 'check_violation';
    END IF;
    p_debit := COALESCE(p_debit, 0);
    p_credit := COALESCE(p_credit, 0);
    IF p_debit < 0 OR p_credit < 0 OR (p_debit > 0 AND p_credit > 0) THEN
        RAISE EXCEPTION 'an opening balance is either a debit or a credit amount, never both' USING ERRCODE = 'check_violation';
    END IF;
    p_entry_date := COALESCE(p_entry_date, CURRENT_DATE);

    IF p_party IS NOT NULL THEN
        PERFORM 1 FROM party.parties p WHERE p.company_id = p_company AND p.id = p_party AND NOT p.is_deleted;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'party % not found', p_party USING ERRCODE = 'no_data_found';
        END IF;
        v_target := acct.role_account(p_company, 'PARTY_CONTROL');
        v_store  := COALESCE(p_store, v_head_office);
        v_source := p_party;
    ELSE
        SELECT * INTO v_account FROM acct.accounts a WHERE a.company_id = p_company AND a.id = p_account;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'account % not found', p_account USING ERRCODE = 'no_data_found';
        END IF;
        IF NOT v_account.is_active OR NOT v_account.is_postable OR v_account.is_party_control THEN
            RAISE EXCEPTION 'account % cannot hold an opening balance (inactive, not postable or the party control account)', v_account.code
                USING ERRCODE = 'check_violation';
        END IF;
        IF v_account.system_role IN ('STOCK', 'TRANSIT', 'OPENING_EQUITY') THEN
            RAISE EXCEPTION 'account % (%) is managed by the system: enter opening stock through inventory', v_account.code, v_account.system_role
                USING ERRCODE = 'check_violation';
        END IF;
        v_target := v_account.id;
        v_store  := v_account.store_id;
        v_source := v_account.id;
    END IF;

    v_equity := acct.role_account(p_company, 'OPENING_EQUITY');

    -- The live opening entry of this account or party: an opening entry that has not been reversed yet.
    SELECT e.* INTO v_live
      FROM acct.journal_entries e
     WHERE e.company_id = p_company AND e.source_type = 'OPENING' AND e.source_id = v_source AND e.reversal_of IS NULL
       AND NOT EXISTS (SELECT 1 FROM acct.journal_entries r WHERE r.company_id = e.company_id AND r.reversal_of = e.id)
     ORDER BY e.posted_at DESC
     LIMIT 1;
    IF FOUND THEN
        SELECT COALESCE(sum(l.debit), 0), COALESCE(sum(l.credit), 0) INTO v_live_debit, v_live_credit
          FROM acct.journal_lines l
         WHERE l.company_id = p_company AND l.entry_id = v_live.id AND l.entry_date = v_live.entry_date
           AND l.account_id = v_target AND l.party_id IS NOT DISTINCT FROM p_party;
        IF v_live_debit = p_debit AND v_live_credit = p_credit AND v_live.entry_date = p_entry_date AND v_live.store_id = v_store THEN
            RETURN v_live.id;     -- nothing changed: saving the same screen twice posts nothing twice
        END IF;
        PERFORM acct.reverse_entry(p_company, v_live.id, 'OPENING_REVERSAL', v_source, 'Opening balance replaced');
    END IF;

    IF p_debit = 0 AND p_credit = 0 THEN
        RETURN NULL;
    END IF;

    INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
    VALUES (p_company, p_entry_date, v_store, 'OPENING', v_source, 'Opening balance', core.current_user_id())
    RETURNING id INTO v_entry;
    v_line := acct.add_line(p_company, v_entry, p_entry_date, v_line, v_store, v_target, p_party, p_debit, p_credit, 'Opening balance');
    v_line := acct.add_line(p_company, v_entry, p_entry_date, v_line, v_store, v_equity, NULL, p_credit, p_debit, 'Opening balance equity');
    RETURN v_entry;
END $$;
GRANT EXECUTE ON FUNCTION acct.post_opening_balance(uuid, uuid, uuid, uuid, core.money, core.money, date) TO pos_app;
GRANT EXECUTE ON FUNCTION acct.next_account_code(uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION party.next_party_code(uuid) TO pos_app;

-- The opening balances that are in force now (entries that are not reversed), one row per account or party.
CREATE VIEW acct.v_live_opening_balances WITH (security_invoker = true) AS
SELECT e.company_id, e.id AS entry_id, e.entry_date, e.store_id, e.source_id,
       l.account_id, l.party_id, l.debit, l.credit
  FROM acct.journal_entries e
  JOIN acct.journal_lines l ON l.company_id = e.company_id AND l.entry_id = e.id AND l.entry_date = e.entry_date
  JOIN acct.accounts a ON a.company_id = l.company_id AND a.id = l.account_id AND a.system_role IS DISTINCT FROM 'OPENING_EQUITY'
 WHERE e.source_type = 'OPENING' AND e.reversal_of IS NULL
   AND NOT EXISTS (SELECT 1 FROM acct.journal_entries r WHERE r.company_id = e.company_id AND r.reversal_of = e.id);
GRANT SELECT ON acct.v_live_opening_balances TO pos_app;
