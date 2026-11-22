-- Rolls back 0200: removes the opening-balance, party-code and account-group objects and restores the baseline default chart seeding.
-- Journal entries already posted by acct.post_opening_balance stay (the journal is append-only); only the helper objects go.
DROP VIEW IF EXISTS acct.v_live_opening_balances;
DROP FUNCTION IF EXISTS acct.post_opening_balance(uuid, uuid, uuid, uuid, core.money, core.money, date);
DROP FUNCTION IF EXISTS party.next_party_code(uuid);
DROP TABLE IF EXISTS party.party_code_counters;
DROP FUNCTION IF EXISTS acct.next_account_code(uuid, text);

-- Baseline body of acct.seed_default_chart (db/schema_v7_uuid.sql), without the group inserts.
CREATE OR REPLACE FUNCTION acct.seed_default_chart(p_company uuid) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
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

DROP TRIGGER IF EXISTS trg_no_system_delete ON acct.accounts;
DROP FUNCTION IF EXISTS acct.forbid_deleting_system_account();
REVOKE DELETE ON acct.accounts FROM pos_app;
DROP TABLE IF EXISTS acct.account_groups;
DROP FUNCTION IF EXISTS acct.forbid_deleting_group_in_use();
DROP FUNCTION IF EXISTS acct.default_group_name(text);
