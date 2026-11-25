-- 0900_split_party_into_customers_vendors_and_staff
-- Decision D-013: the "party" wrapper is replaced by separate customers, vendors and staff.
--
-- Everything below runs in ONE transaction (the runner wraps each file). Same ids are kept: a customer, vendor or staff member
-- keeps the uuid it had as a party, so every document, voucher and journal line still points at the same person.
--
-- Dropped on purpose (decision D-013, field list approved by the owner), non-empty values are counted in the NOTICE output:
--   party.parties: name_ur, father_name, cnic, phone_secondary, address, attributes (jsonb: notes), is_customer/is_vendor/is_staff
--   credit_limit on customers (kept on vendors only: purch.check_vendor_credit and the vendor summary use it)
--   Nothing else is lost; the pre-migration dump of the database is the way back for these values.
--
-- The ledger keeps ONE control account (system_role PARTY_CONTROL becomes SUBLEDGER_CONTROL, flag is_party_control becomes
-- is_subledger_control). Journal lines and vouchers name their owner in three nullable columns (customer_id, vendor_id, staff_id,
-- at most one per row). Contacts balances are kept per type (contacts.customer_balances, vendor_balances, staff_balances).
--
-- A person who was a party with several roles (customer and vendor, ...) now exists once in each table with the same id.
-- Their old ledger lines are assigned to one type by the source of the journal entry (sale = customer, purchase = vendor,
-- payroll = staff), otherwise by priority customer, vendor, staff.
--
-- Requires a role that is superuser or bypasses row-level security (the migration owner), like the earlier migrations.

DO $$
BEGIN
    IF NOT (SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user) THEN
        RAISE EXCEPTION 'migration 0900_split_party_into_customers_vendors_and_staff must run as a role that bypasses row-level security (the schema owner)';
    END IF;
    IF EXISTS (SELECT 1 FROM party.parties p WHERE p.is_staff AND NOT EXISTS
               (SELECT 1 FROM party.staff_profiles s WHERE s.company_id = p.company_id AND s.party_id = p.id)) THEN
        RAISE EXCEPTION 'a staff party has no staff profile: fix the data before splitting';
    END IF;
    IF EXISTS (SELECT 1 FROM party.staff_profiles s WHERE NOT EXISTS
               (SELECT 1 FROM party.parties p WHERE p.company_id = s.company_id AND p.id = s.party_id AND p.is_staff)) THEN
        RAISE EXCEPTION 'a staff profile belongs to a party that is not staff: fix the data before splitting';
    END IF;
END $$;

DO $$
DECLARE r record;
BEGIN
    SELECT count(*) FILTER (WHERE name_ur IS NOT NULL) AS name_ur, count(*) FILTER (WHERE father_name IS NOT NULL) AS father_name,
           count(*) FILTER (WHERE cnic IS NOT NULL) AS cnic, count(*) FILTER (WHERE phone_secondary IS NOT NULL) AS phone_secondary,
           count(*) FILTER (WHERE address IS NOT NULL) AS address, count(*) FILTER (WHERE attributes <> '{}'::jsonb) AS attributes,
           count(*) FILTER (WHERE credit_limit IS NOT NULL AND is_customer) AS customer_credit_limit,
           count(*) AS total
      INTO r FROM party.parties;
    RAISE NOTICE '0900_split_party_into_customers_vendors_and_staff: dropped columns with data: name_ur %, father_name %, cnic %, phone_secondary %, address %, attributes %, customer credit_limit % (of % rows)',
        r.name_ur, r.father_name, r.cnic, r.phone_secondary, r.address, r.attributes, r.customer_credit_limit, r.total;
END $$;

-- 1. Views that mention the party columns go first; they are created again at the end
DROP VIEW purch.v_vendor_summary;
DROP VIEW purch.v_vendor_order_lines;
DROP VIEW purch.v_vendor_due;
DROP VIEW purch.v_invoice_balance;
DROP VIEW hr.v_salary_outstanding;
DROP VIEW hr.v_commission_summary;
DROP VIEW acct.v_live_opening_balances;

-- 2. New tables
CREATE SCHEMA contacts;
GRANT USAGE ON SCHEMA contacts TO pos_app;

CREATE TABLE contacts.customers (
    company_id  uuid        NOT NULL REFERENCES platform.companies (id),
    id          uuid        NOT NULL DEFAULT uuidv7(),
    code        text        NOT NULL,
    name        text        NOT NULL,
    phone       text,
    email       text,
    birth_date  date,
    city_id     uuid,
    status      text        NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE', 'INACTIVE')),
    change_seq  bigint      NOT NULL DEFAULT nextval('core.change_seq'),
    is_deleted  boolean     NOT NULL DEFAULT false,
    deleted_at  timestamptz,
    deleted_by  uuid,
    created_at  timestamptz NOT NULL DEFAULT now(),
    created_by  uuid,
    updated_at  timestamptz,
    updated_by  uuid,
    row_ver     bigint      NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, city_id) REFERENCES master.cities (company_id, id)
);
CREATE UNIQUE INDEX ux_customers_code ON contacts.customers (company_id, code) WHERE NOT is_deleted;
CREATE INDEX ix_customers_name ON contacts.customers (company_id, lower(name)) WHERE NOT is_deleted;
CREATE INDEX ix_customers_phone ON contacts.customers (company_id, phone) WHERE phone IS NOT NULL;

CREATE TABLE contacts.vendors (
    company_id  uuid        NOT NULL REFERENCES platform.companies (id),
    id          uuid        NOT NULL DEFAULT uuidv7(),
    code        text        NOT NULL,
    name        text        NOT NULL,
    phone       text,
    email       text,
    birth_date  date,
    city_id     uuid,
    credit_limit core.money CHECK (credit_limit IS NULL OR (credit_limit)::bigint >= 0),
    status      text        NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE', 'INACTIVE')),
    change_seq  bigint      NOT NULL DEFAULT nextval('core.change_seq'),
    is_deleted  boolean     NOT NULL DEFAULT false,
    deleted_at  timestamptz,
    deleted_by  uuid,
    created_at  timestamptz NOT NULL DEFAULT now(),
    created_by  uuid,
    updated_at  timestamptz,
    updated_by  uuid,
    row_ver     bigint      NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, city_id) REFERENCES master.cities (company_id, id)
);
CREATE UNIQUE INDEX ux_vendors_code ON contacts.vendors (company_id, code) WHERE NOT is_deleted;
CREATE INDEX ix_vendors_name ON contacts.vendors (company_id, lower(name)) WHERE NOT is_deleted;
CREATE INDEX ix_vendors_phone ON contacts.vendors (company_id, phone) WHERE phone IS NOT NULL;

CREATE TABLE contacts.staff (
    company_id  uuid        NOT NULL REFERENCES platform.companies (id),
    id          uuid        NOT NULL DEFAULT uuidv7(),
    code        text        NOT NULL,
    name        text        NOT NULL,
    phone       text,
    email       text,
    birth_date  date,
    city_id     uuid,
    designation text,
    is_manager  boolean     NOT NULL DEFAULT false,
    base_salary core.money  NOT NULL DEFAULT 0 CHECK ((base_salary)::bigint >= 0),
    pay_frequency text      NOT NULL DEFAULT 'MONTHLY' CHECK (pay_frequency IN ('MONTHLY', 'BIWEEKLY', 'WEEKLY')),
    current_store_id uuid   NOT NULL CHECK (current_store_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    joined_on   date,
    left_on     date,
    status      text        NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE', 'INACTIVE')),
    change_seq  bigint      NOT NULL DEFAULT nextval('core.change_seq'),
    is_deleted  boolean     NOT NULL DEFAULT false,
    deleted_at  timestamptz,
    deleted_by  uuid,
    created_at  timestamptz NOT NULL DEFAULT now(),
    created_by  uuid,
    updated_at  timestamptz,
    updated_by  uuid,
    row_ver     bigint      NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, city_id) REFERENCES master.cities (company_id, id),
    FOREIGN KEY (company_id, current_store_id) REFERENCES core.stores (company_id, id)
);
CREATE UNIQUE INDEX ux_staff_code ON contacts.staff (company_id, code) WHERE NOT is_deleted;
CREATE INDEX ix_staff_name ON contacts.staff (company_id, lower(name)) WHERE NOT is_deleted;
CREATE INDEX ix_staff_phone ON contacts.staff (company_id, phone) WHERE phone IS NOT NULL;

CREATE TABLE contacts.staff_store_history (
    company_id    uuid        NOT NULL REFERENCES platform.companies (id),
    id            uuid        NOT NULL DEFAULT uuidv7(),
    staff_id      uuid        NOT NULL,
    from_store_id uuid,
    to_store_id   uuid        NOT NULL,
    effective_on  date        NOT NULL,
    changed_by    uuid,
    changed_at    timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    CHECK (from_store_id IS NULL OR from_store_id <> to_store_id),
    FOREIGN KEY (company_id, staff_id) REFERENCES contacts.staff (company_id, id),
    FOREIGN KEY (company_id, from_store_id) REFERENCES core.stores (company_id, id),
    FOREIGN KEY (company_id, to_store_id) REFERENCES core.stores (company_id, id)
);
CREATE INDEX ix_staff_history ON contacts.staff_store_history (company_id, staff_id, effective_on);

CREATE TABLE contacts.customer_balances (
    company_id      uuid        NOT NULL REFERENCES platform.companies (id),
    customer_id  uuid        NOT NULL,
    debit_total     core.money  NOT NULL DEFAULT 0,
    credit_total    core.money  NOT NULL DEFAULT 0,
    balance         core.money  GENERATED ALWAYS AS ((debit_total)::bigint - (credit_total)::bigint) STORED,
    last_entry_date date,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    change_seq      bigint      NOT NULL DEFAULT nextval('core.change_seq'),
    PRIMARY KEY (company_id, customer_id),
    FOREIGN KEY (company_id, customer_id) REFERENCES contacts.customers (company_id, id)
);

CREATE TABLE contacts.vendor_balances (
    company_id      uuid        NOT NULL REFERENCES platform.companies (id),
    vendor_id  uuid        NOT NULL,
    debit_total     core.money  NOT NULL DEFAULT 0,
    credit_total    core.money  NOT NULL DEFAULT 0,
    balance         core.money  GENERATED ALWAYS AS ((debit_total)::bigint - (credit_total)::bigint) STORED,
    last_entry_date date,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    change_seq      bigint      NOT NULL DEFAULT nextval('core.change_seq'),
    PRIMARY KEY (company_id, vendor_id),
    FOREIGN KEY (company_id, vendor_id) REFERENCES contacts.vendors (company_id, id)
);

CREATE TABLE contacts.staff_balances (
    company_id      uuid        NOT NULL REFERENCES platform.companies (id),
    staff_id  uuid        NOT NULL,
    debit_total     core.money  NOT NULL DEFAULT 0,
    credit_total    core.money  NOT NULL DEFAULT 0,
    balance         core.money  GENERATED ALWAYS AS ((debit_total)::bigint - (credit_total)::bigint) STORED,
    last_entry_date date,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    change_seq      bigint      NOT NULL DEFAULT nextval('core.change_seq'),
    PRIMARY KEY (company_id, staff_id),
    FOREIGN KEY (company_id, staff_id) REFERENCES contacts.staff (company_id, id)
);

CREATE TABLE contacts.code_counters (
    company_id uuid   NOT NULL REFERENCES platform.companies (id) PRIMARY KEY,
    last_value bigint NOT NULL DEFAULT 0 CHECK (last_value >= 0)
);

-- Customers, vendors and staff share one code counter so that a code is never used twice in a company.
CREATE FUNCTION contacts.next_code(p_company uuid) RETURNS text
LANGUAGE plpgsql AS $function$
DECLARE
    v_value     bigint;
    v_candidate text;
BEGIN
    LOOP
        INSERT INTO contacts.code_counters (company_id, last_value) VALUES (p_company, 1)
        ON CONFLICT (company_id) DO UPDATE SET last_value = contacts.code_counters.last_value + 1
        RETURNING last_value INTO v_value;
        IF v_value > 99999 THEN
            RAISE EXCEPTION 'no free codes left for company %', p_company USING ERRCODE = 'check_violation';
        END IF;
        v_candidate := '103' || lpad(v_value::text, 5, '0');
        -- skip a code that was typed by hand or imported earlier
        EXIT WHEN NOT EXISTS (SELECT 1 FROM contacts.customers x WHERE x.company_id = p_company AND x.code = v_candidate)
              AND NOT EXISTS (SELECT 1 FROM contacts.vendors x WHERE x.company_id = p_company AND x.code = v_candidate)
              AND NOT EXISTS (SELECT 1 FROM contacts.staff x WHERE x.company_id = p_company AND x.code = v_candidate);
    END LOOP;
    RETURN v_candidate;
END $function$;

-- Does the named customer, vendor or staff member exist (and is not deleted)?
CREATE FUNCTION acct.contact_exists(p_company uuid, p_kind text, p_id uuid) RETURNS boolean
LANGUAGE sql STABLE AS $function$
    SELECT CASE p_kind
        WHEN 'CUSTOMER' THEN EXISTS (SELECT 1 FROM contacts.customers c WHERE c.company_id = p_company AND c.id = p_id AND NOT c.is_deleted)
        WHEN 'VENDOR'   THEN EXISTS (SELECT 1 FROM contacts.vendors c WHERE c.company_id = p_company AND c.id = p_id AND NOT c.is_deleted)
        WHEN 'STAFF'    THEN EXISTS (SELECT 1 FROM contacts.staff c WHERE c.company_id = p_company AND c.id = p_id AND NOT c.is_deleted)
        ELSE false
    END
$function$;

-- Used only while splitting old rows: which type does a ledger row of a party belong to? (dropped at the end)
CREATE FUNCTION contacts.classify_legacy(p_company uuid, p_id uuid, p_source text) RETURNS text
LANGUAGE plpgsql STABLE AS $function$
DECLARE
    v_customer boolean; v_vendor boolean; v_staff boolean;
BEGIN
    SELECT is_customer, is_vendor, is_staff INTO v_customer, v_vendor, v_staff
      FROM party.parties WHERE company_id = p_company AND id = p_id;
    IF NOT FOUND THEN RETURN NULL; END IF;
    IF p_source LIKE 'SALE%' AND v_customer THEN RETURN 'CUSTOMER'; END IF;
    IF p_source LIKE 'PURCHASE%' AND v_vendor THEN RETURN 'VENDOR'; END IF;
    IF (p_source LIKE 'PAYROLL%' OR p_source LIKE 'SALARY%') AND v_staff THEN RETURN 'STAFF'; END IF;
    IF v_customer THEN RETURN 'CUSTOMER'; END IF;
    IF v_vendor THEN RETURN 'VENDOR'; END IF;
    RETURN 'STAFF';
END $function$;

-- 3. Copy the rows with the same ids
INSERT INTO contacts.customers (company_id, id, code, name, phone, email, city_id, status, change_seq, is_deleted, deleted_at, deleted_by, created_at, created_by, updated_at, updated_by, row_ver)
SELECT p.company_id, p.id, p.code, p.name, p.phone_primary, p.email, p.city_id, p.status, p.change_seq, p.is_deleted, p.deleted_at, p.deleted_by, p.created_at, p.created_by, p.updated_at, p.updated_by, p.row_ver FROM party.parties p WHERE p.is_customer;

INSERT INTO contacts.vendors (company_id, id, code, name, phone, email, city_id, status, change_seq, is_deleted, deleted_at, deleted_by, created_at, created_by, updated_at, updated_by, row_ver, credit_limit)
SELECT p.company_id, p.id, p.code, p.name, p.phone_primary, p.email, p.city_id, p.status, p.change_seq, p.is_deleted, p.deleted_at, p.deleted_by, p.created_at, p.created_by, p.updated_at, p.updated_by, p.row_ver, p.credit_limit FROM party.parties p WHERE p.is_vendor;

INSERT INTO contacts.staff (company_id, id, code, name, phone, email, city_id, status, change_seq, is_deleted, deleted_at, deleted_by, created_at, created_by, updated_at, updated_by, row_ver, designation, is_manager, base_salary, pay_frequency, current_store_id, joined_on, left_on)
SELECT p.company_id, p.id, p.code, p.name, p.phone_primary, p.email, p.city_id, p.status, p.change_seq, p.is_deleted, p.deleted_at, p.deleted_by, p.created_at, p.created_by, p.updated_at, p.updated_by, p.row_ver, s.designation, s.is_manager, s.base_salary, s.pay_frequency, s.current_store_id, s.joined_on, s.left_on
  FROM party.parties p
  JOIN party.staff_profiles s ON s.company_id = p.company_id AND s.party_id = p.id
 WHERE p.is_staff;

INSERT INTO contacts.staff_store_history (company_id, id, staff_id, from_store_id, to_store_id, effective_on, changed_by, changed_at)
SELECT company_id, id, party_id, from_store_id, to_store_id, effective_on, changed_by, changed_at FROM party.staff_store_history;

INSERT INTO contacts.code_counters (company_id, last_value)
SELECT company_id, last_value FROM party.party_code_counters;

DO $$
BEGIN
    IF (SELECT count(*) FROM contacts.customers) <> (SELECT count(*) FROM party.parties WHERE is_customer)
       OR (SELECT count(*) FROM contacts.vendors) <> (SELECT count(*) FROM party.parties WHERE is_vendor)
       OR (SELECT count(*) FROM contacts.staff) <> (SELECT count(*) FROM party.parties WHERE is_staff)
       OR (SELECT count(*) FROM contacts.staff_store_history) <> (SELECT count(*) FROM party.staff_store_history) THEN
        RAISE EXCEPTION 'row counts differ after copying parties to customers, vendors and staff';
    END IF;
END $$;

-- 4. Ledger: journal lines and vouchers name the owner in three columns
-- Journal lines are append-only (trigger trg_no_change on the table and on every monthly partition): it is switched off for
-- this one correction and switched on again before the migration ends.
ALTER TABLE acct.journal_lines DISABLE TRIGGER trg_no_change;
ALTER TABLE acct.journal_lines ADD COLUMN customer_id uuid, ADD COLUMN vendor_id uuid, ADD COLUMN staff_id uuid;

UPDATE acct.journal_lines l
   SET customer_id = CASE WHEN x.kind = 'CUSTOMER' THEN l.party_id END,
       vendor_id   = CASE WHEN x.kind = 'VENDOR'   THEN l.party_id END,
       staff_id    = CASE WHEN x.kind = 'STAFF'    THEN l.party_id END
  FROM (SELECT l2.company_id, l2.entry_id, l2.entry_date, l2.line_no,
               contacts.classify_legacy(l2.company_id, l2.party_id, e.source_type) AS kind
          FROM acct.journal_lines l2
          JOIN acct.journal_entries e ON e.company_id = l2.company_id AND e.id = l2.entry_id AND e.entry_date = l2.entry_date
         WHERE l2.party_id IS NOT NULL) x
 WHERE l.company_id = x.company_id AND l.entry_id = x.entry_id AND l.entry_date = x.entry_date AND l.line_no = x.line_no;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM acct.journal_lines WHERE party_id IS NOT NULL AND num_nonnulls(customer_id, vendor_id, staff_id) <> 1) THEN
        RAISE EXCEPTION 'a journal line with a party was not assigned to a customer, vendor or staff member';
    END IF;
END $$;

ALTER TABLE acct.journal_lines DROP COLUMN party_id;
ALTER TABLE acct.journal_lines
    ADD CONSTRAINT journal_lines_one_contact_chk CHECK (num_nonnulls(customer_id, vendor_id, staff_id) <= 1),
    ADD FOREIGN KEY (company_id, customer_id) REFERENCES contacts.customers (company_id, id),
    ADD FOREIGN KEY (company_id, vendor_id) REFERENCES contacts.vendors (company_id, id),
    ADD FOREIGN KEY (company_id, staff_id) REFERENCES contacts.staff (company_id, id);
CREATE INDEX ix_jl_customer ON acct.journal_lines (company_id, customer_id, entry_date) WHERE customer_id IS NOT NULL;
CREATE INDEX ix_jl_vendor ON acct.journal_lines (company_id, vendor_id, entry_date) WHERE vendor_id IS NOT NULL;
CREATE INDEX ix_jl_staff ON acct.journal_lines (company_id, staff_id, entry_date) WHERE staff_id IS NOT NULL;
ALTER TABLE acct.journal_lines ENABLE TRIGGER trg_no_change;

-- Balances rebuilt per type from the lines (same totals as the old party balances)
INSERT INTO contacts.customer_balances (company_id, customer_id, debit_total, credit_total, last_entry_date)
SELECT company_id, customer_id, sum(debit), sum(credit), max(entry_date) FROM acct.journal_lines WHERE customer_id IS NOT NULL GROUP BY company_id, customer_id;
INSERT INTO contacts.vendor_balances (company_id, vendor_id, debit_total, credit_total, last_entry_date)
SELECT company_id, vendor_id, sum(debit), sum(credit), max(entry_date) FROM acct.journal_lines WHERE vendor_id IS NOT NULL GROUP BY company_id, vendor_id;
INSERT INTO contacts.staff_balances (company_id, staff_id, debit_total, credit_total, last_entry_date)
SELECT company_id, staff_id, sum(debit), sum(credit), max(entry_date) FROM acct.journal_lines WHERE staff_id IS NOT NULL GROUP BY company_id, staff_id;

DO $$
BEGIN
    IF (SELECT COALESCE(sum(balance), 0) FROM contacts.customer_balances) + (SELECT COALESCE(sum(balance), 0) FROM contacts.vendor_balances)
       + (SELECT COALESCE(sum(balance), 0) FROM contacts.staff_balances) <> (SELECT COALESCE(sum(balance), 0) FROM party.party_balances) THEN
        RAISE EXCEPTION 'the rebuilt balances do not add up to the old party balances';
    END IF;
END $$;

ALTER TABLE acct.vouchers DISABLE TRIGGER USER;
ALTER TABLE acct.vouchers ADD COLUMN customer_id uuid, ADD COLUMN vendor_id uuid, ADD COLUMN staff_id uuid;
UPDATE acct.vouchers v
   SET customer_id = l.customer_id, vendor_id = l.vendor_id, staff_id = l.staff_id
  FROM (SELECT DISTINCT company_id, entry_id, customer_id, vendor_id, staff_id FROM acct.journal_lines
         WHERE num_nonnulls(customer_id, vendor_id, staff_id) = 1) l
 WHERE v.party_id IS NOT NULL AND l.company_id = v.company_id AND l.entry_id = v.journal_entry_id;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM acct.vouchers WHERE party_id IS NOT NULL AND num_nonnulls(customer_id, vendor_id, staff_id) <> 1) THEN
        RAISE EXCEPTION 'a voucher with a party was not assigned to a customer, vendor or staff member';
    END IF;
END $$;
ALTER TABLE acct.vouchers DROP COLUMN party_id;
ALTER TABLE acct.vouchers
    ADD CONSTRAINT vouchers_one_contact_chk CHECK (num_nonnulls(customer_id, vendor_id, staff_id) <= 1),
    ADD FOREIGN KEY (company_id, customer_id) REFERENCES contacts.customers (company_id, id),
    ADD FOREIGN KEY (company_id, vendor_id) REFERENCES contacts.vendors (company_id, id),
    ADD FOREIGN KEY (company_id, staff_id) REFERENCES contacts.staff (company_id, id);
CREATE INDEX ix_vouchers_customer ON acct.vouchers (company_id, customer_id) WHERE customer_id IS NOT NULL;
CREATE INDEX ix_vouchers_vendor ON acct.vouchers (company_id, vendor_id) WHERE vendor_id IS NOT NULL;
CREATE INDEX ix_vouchers_staff ON acct.vouchers (company_id, staff_id) WHERE staff_id IS NOT NULL;
ALTER TABLE acct.vouchers ENABLE TRIGGER USER;

-- 5. Other tables: the column keeps its data and its ids, only its name and its foreign key change
DO $$
DECLARE
    m  record;
    c  record;
    v_attnum smallint;
BEGIN
    FOR m IN SELECT * FROM (VALUES
        ('sec.users', 'staff_party_id', 'staff_id', 'contacts.staff'),
        ('cash.promises', 'customer_party_id', 'customer_id', 'contacts.customers'),
        ('hr.commission_entries', 'employee_party_id', 'staff_id', 'contacts.staff'),
        ('hr.commission_plan_members', 'employee_party_id', 'staff_id', 'contacts.staff'),
        ('hr.payroll_adjustments', 'employee_party_id', 'staff_id', 'contacts.staff'),
        ('hr.payroll_lines', 'employee_party_id', 'staff_id', 'contacts.staff'),
        ('hr.salary_payments', 'employee_party_id', 'staff_id', 'contacts.staff'),
        ('hr.salary_terms', 'employee_party_id', 'staff_id', 'contacts.staff'),
        ('inv.documents', 'party_id', 'vendor_id', 'contacts.vendors'),
        ('purch.invoices', 'vendor_party_id', 'vendor_id', 'contacts.vendors'),
        ('purch.vendor_payment_schedule', 'vendor_party_id', 'vendor_id', 'contacts.vendors'),
        ('purch.vendor_articles', 'vendor_party_id', 'vendor_id', 'contacts.vendors'),
        ('sales.invoices', 'customer_party_id', 'customer_id', 'contacts.customers'),
        ('sales.invoices', 'salesman_party_id', 'salesman_staff_id', 'contacts.staff'),
        ('sales.invoice_lines', 'salesman_party_id', 'salesman_staff_id', NULL)
    ) AS t(tbl, old_col, new_col, target)
    LOOP
        SELECT attnum INTO v_attnum FROM pg_attribute WHERE attrelid = m.tbl::regclass AND attname = m.old_col AND NOT attisdropped;
        FOR c IN SELECT conname FROM pg_constraint
                  WHERE conrelid = m.tbl::regclass AND contype = 'f' AND v_attnum = ANY (conkey) AND confrelid = 'party.parties'::regclass
        LOOP
            EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', m.tbl, c.conname);
        END LOOP;
        EXECUTE format('ALTER TABLE %s RENAME COLUMN %I TO %I', m.tbl, m.old_col, m.new_col);
        IF m.target IS NOT NULL THEN
            EXECUTE format('ALTER TABLE %s ADD FOREIGN KEY (company_id, %I) REFERENCES %s (company_id, id)', m.tbl, m.new_col, m.target);
        END IF;
        -- indexes and constraints that carry the old column name in their own name
        FOR c IN SELECT i.relname AS name, 'i' AS kind FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid
                  WHERE x.indrelid IN (SELECT m.tbl::regclass::oid UNION SELECT relid::oid FROM pg_partition_tree(m.tbl::regclass)) AND i.relname LIKE '%' || m.old_col || '%'
                 UNION ALL
                 SELECT k.conname, 'c' FROM pg_constraint k
                  WHERE k.conrelid IN (SELECT m.tbl::regclass::oid UNION SELECT relid::oid FROM pg_partition_tree(m.tbl::regclass)) AND k.conname LIKE '%' || m.old_col || '%'
                    AND k.contype IN ('n', 'u', 'p', 'c')
        LOOP
            IF c.kind = 'i' AND EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conname = c.name AND k.contype IN ('u', 'p')) THEN CONTINUE; END IF;
            IF c.kind = 'i' THEN
                EXECUTE format('ALTER INDEX %s RENAME TO %I', (SELECT i.oid::regclass FROM pg_class i WHERE i.relname = c.name AND i.relkind IN ('i', 'I') AND i.relnamespace = (SELECT relnamespace FROM pg_class WHERE oid = m.tbl::regclass)), replace(c.name, m.old_col, m.new_col));
            ELSE
                EXECUTE format('ALTER TABLE %s RENAME CONSTRAINT %I TO %I', (SELECT conrelid::regclass FROM pg_constraint WHERE conname = c.name AND conrelid IN (SELECT m.tbl::regclass::oid UNION SELECT relid::oid FROM pg_partition_tree(m.tbl::regclass)) LIMIT 1), c.name, replace(c.name, m.old_col, m.new_col));
            END IF;
        END LOOP;
    END LOOP;
END $$;

-- orders: one column for the customer or the vendor, chosen by the order type
ALTER TABLE orders.orders DISABLE TRIGGER USER;
ALTER TABLE orders.orders ADD COLUMN customer_id uuid, ADD COLUMN vendor_id uuid;
UPDATE orders.orders o
   SET customer_id = CASE WHEN k.kind = 'CUSTOMER' THEN o.party_id END,
       vendor_id   = CASE WHEN k.kind = 'VENDOR'   THEN o.party_id END
  FROM (SELECT o2.company_id, o2.id,
               contacts.classify_legacy(o2.company_id, o2.party_id,
                   CASE WHEN o2.order_type IN ('SALE', 'SALE_RETURN') THEN 'SALE'
                        WHEN o2.order_type IN ('PURCHASE', 'PURCHASE_RETURN', 'VENDOR_PO') THEN 'PURCHASE' ELSE 'COMPANY_PO' END) AS kind
          FROM orders.orders o2 WHERE o2.party_id IS NOT NULL) k
 WHERE o.company_id = k.company_id AND o.id = k.id;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM orders.orders WHERE party_id IS NOT NULL AND num_nonnulls(customer_id, vendor_id) <> 1) THEN
        RAISE EXCEPTION 'an order belongs to a staff member only: it has no customer or vendor to move to';
    END IF;
END $$;
ALTER TABLE orders.orders DROP COLUMN party_id;
ALTER TABLE orders.orders
    ADD CONSTRAINT orders_one_contact_chk CHECK (num_nonnulls(customer_id, vendor_id) <= 1),
    ADD FOREIGN KEY (company_id, customer_id) REFERENCES contacts.customers (company_id, id),
    ADD FOREIGN KEY (company_id, vendor_id) REFERENCES contacts.vendors (company_id, id);
ALTER TABLE orders.orders ENABLE TRIGGER USER;

-- expense payees: a vendor or a staff member (a customer cannot be paid an expense)
ALTER TABLE exp.expenses DISABLE TRIGGER USER;
ALTER TABLE exp.expenses ADD COLUMN payee_vendor_id uuid, ADD COLUMN payee_staff_id uuid;
UPDATE exp.expenses x
   SET payee_vendor_id = CASE WHEN EXISTS (SELECT 1 FROM contacts.vendors v WHERE v.company_id = x.company_id AND v.id = x.payee_party_id) THEN x.payee_party_id END,
       payee_staff_id  = CASE WHEN NOT EXISTS (SELECT 1 FROM contacts.vendors v WHERE v.company_id = x.company_id AND v.id = x.payee_party_id)
                               AND EXISTS (SELECT 1 FROM contacts.staff s WHERE s.company_id = x.company_id AND s.id = x.payee_party_id) THEN x.payee_party_id END
 WHERE x.payee_party_id IS NOT NULL;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM exp.expenses WHERE payee_party_id IS NOT NULL AND num_nonnulls(payee_vendor_id, payee_staff_id) <> 1) THEN
        RAISE EXCEPTION 'an expense payee is a customer only: it cannot move to vendors or staff';
    END IF;
END $$;
ALTER TABLE exp.expenses DROP COLUMN payee_party_id;
ALTER TABLE exp.expenses
    ADD CONSTRAINT expenses_one_payee_chk CHECK (num_nonnulls(payee_vendor_id, payee_staff_id) <= 1),
    ADD FOREIGN KEY (company_id, payee_vendor_id) REFERENCES contacts.vendors (company_id, id),
    ADD FOREIGN KEY (company_id, payee_staff_id) REFERENCES contacts.staff (company_id, id);
ALTER TABLE exp.expenses ENABLE TRIGGER USER;

ALTER TABLE exp.recurring_schedules DISABLE TRIGGER USER;
ALTER TABLE exp.recurring_schedules ADD COLUMN payee_vendor_id uuid, ADD COLUMN payee_staff_id uuid;
UPDATE exp.recurring_schedules x
   SET payee_vendor_id = CASE WHEN EXISTS (SELECT 1 FROM contacts.vendors v WHERE v.company_id = x.company_id AND v.id = x.payee_party_id) THEN x.payee_party_id END,
       payee_staff_id  = CASE WHEN NOT EXISTS (SELECT 1 FROM contacts.vendors v WHERE v.company_id = x.company_id AND v.id = x.payee_party_id)
                               AND EXISTS (SELECT 1 FROM contacts.staff s WHERE s.company_id = x.company_id AND s.id = x.payee_party_id) THEN x.payee_party_id END
 WHERE x.payee_party_id IS NOT NULL;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM exp.recurring_schedules WHERE payee_party_id IS NOT NULL AND num_nonnulls(payee_vendor_id, payee_staff_id) <> 1) THEN
        RAISE EXCEPTION 'an expense payee is a customer only: it cannot move to vendors or staff';
    END IF;
END $$;
ALTER TABLE exp.recurring_schedules DROP COLUMN payee_party_id;
ALTER TABLE exp.recurring_schedules
    ADD CONSTRAINT recurring_schedules_one_payee_chk CHECK (num_nonnulls(payee_vendor_id, payee_staff_id) <= 1),
    ADD FOREIGN KEY (company_id, payee_vendor_id) REFERENCES contacts.vendors (company_id, id),
    ADD FOREIGN KEY (company_id, payee_staff_id) REFERENCES contacts.staff (company_id, id);
ALTER TABLE exp.recurring_schedules ENABLE TRIGGER USER;

-- 6. The control account
ALTER TABLE acct.accounts DISABLE TRIGGER USER;
ALTER TABLE acct.accounts RENAME COLUMN is_party_control TO is_subledger_control;
ALTER TABLE acct.accounts RENAME CONSTRAINT accounts_is_party_control_not_null TO accounts_is_subledger_control_not_null;
UPDATE acct.accounts SET system_role = 'SUBLEDGER_CONTROL' WHERE system_role = 'PARTY_CONTROL';
UPDATE acct.accounts SET name = 'Subledger control (customers, vendors, staff)' WHERE name = 'Party control (customers, vendors, staff)';
ALTER TABLE acct.accounts ENABLE TRIGGER USER;
ALTER TABLE acct.account_groups DISABLE TRIGGER USER;
UPDATE acct.account_groups SET name = 'Subledger control' WHERE group_code = '103' AND name = 'Party control';
ALTER TABLE acct.account_groups ENABLE TRIGGER USER;

-- 7. Triggers, row-level security and grants of the new tables (after the copy, so the copy writes no audit rows)
DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['contacts.customers', 'contacts.vendors', 'contacts.staff'] LOOP
        EXECUTE format('CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON %s FOR EACH ROW EXECUTE FUNCTION audit.log_row_change()', t);
        EXECUTE format('CREATE TRIGGER trg_seq BEFORE UPDATE ON %s FOR EACH ROW EXECUTE FUNCTION core.bump_change_seq()', t);
        EXECUTE format('CREATE TRIGGER trg_touch BEFORE UPDATE ON %s FOR EACH ROW EXECUTE FUNCTION core.touch_row()', t);
    END LOOP;
    FOREACH t IN ARRAY ARRAY['contacts.customer_balances', 'contacts.vendor_balances', 'contacts.staff_balances'] LOOP
        EXECUTE format('CREATE TRIGGER trg_seq BEFORE UPDATE ON %s FOR EACH ROW EXECUTE FUNCTION core.bump_change_seq()', t);
    END LOOP;
    FOREACH t IN ARRAY ARRAY['contacts.customers', 'contacts.vendors', 'contacts.staff', 'contacts.staff_store_history',
                             'contacts.customer_balances', 'contacts.vendor_balances', 'contacts.staff_balances', 'contacts.code_counters'] LOOP
        PERFORM core.enable_rls(t::regclass, false);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE ON %s TO pos_app', t);
    END LOOP;
END $$;
CREATE TRIGGER trg_salary_term AFTER INSERT OR UPDATE OF base_salary, pay_frequency ON contacts.staff
    FOR EACH ROW EXECUTE FUNCTION hr.log_salary_term();

-- One list of everybody a journal line, voucher or report can name (the kind says which table the id belongs to)
CREATE VIEW contacts.v_contact_directory WITH (security_invoker = true) AS
    SELECT company_id, 'CUSTOMER'::text AS kind_code, id, code, name, phone, status, is_deleted FROM contacts.customers
    UNION ALL
    SELECT company_id, 'VENDOR'::text, id, code, name, phone, status, is_deleted FROM contacts.vendors
    UNION ALL
    SELECT company_id, 'STAFF'::text, id, code, name, phone, status, is_deleted FROM contacts.staff;
GRANT SELECT ON contacts.v_contact_directory TO pos_app;

-- 8. Functions: old signatures go, the rewritten functions are created
DROP FUNCTION acct.post_opening_balance(uuid, uuid, uuid, uuid, core.money, core.money, date);
DROP FUNCTION acct.post_voucher(uuid, uuid, text, date, uuid, uuid, uuid, uuid, core.money, text, text, text, uuid, jsonb);
DROP FUNCTION acct.resolve_voucher_side(uuid, uuid, uuid, text);
DROP FUNCTION acct.add_line(uuid, uuid, date, integer, uuid, uuid, uuid, core.money, core.money, text);
DROP FUNCTION rpt.account_ledger(uuid, uuid, date, date, uuid);
DROP FUNCTION rpt.general_journal(uuid, date, date, uuid);

CREATE OR REPLACE FUNCTION acct.post_opening_balance(p_company uuid, p_account uuid, p_contact_kind text, p_contact_id uuid, p_store uuid, p_debit core.money, p_credit core.money, p_entry_date date)
 RETURNS uuid
 LANGUAGE plpgsql
AS $function$
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
    IF (p_account IS NULL) = (p_contact_id IS NULL) THEN
        RAISE EXCEPTION 'name exactly one of account or contact for an opening balance' USING ERRCODE = 'check_violation';
    END IF;
    p_debit := COALESCE(p_debit, 0);
    p_credit := COALESCE(p_credit, 0);
    IF p_debit < 0 OR p_credit < 0 OR (p_debit > 0 AND p_credit > 0) THEN
        RAISE EXCEPTION 'an opening balance is either a debit or a credit amount, never both' USING ERRCODE = 'check_violation';
    END IF;
    p_entry_date := COALESCE(p_entry_date, CURRENT_DATE);
    IF p_contact_id IS NOT NULL THEN
        PERFORM 1 WHERE acct.contact_exists(p_company, p_contact_kind, p_contact_id);
        IF NOT FOUND THEN
            RAISE EXCEPTION 'contact % not found', p_contact_id USING ERRCODE = 'no_data_found';
        END IF;
        v_target := acct.role_account(p_company, 'SUBLEDGER_CONTROL');
        v_store  := COALESCE(p_store, v_head_office);
        v_source := p_contact_id;
    ELSE
        SELECT * INTO v_account FROM acct.accounts a WHERE a.company_id = p_company AND a.id = p_account;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'account % not found', p_account USING ERRCODE = 'no_data_found';
        END IF;
        IF NOT v_account.is_active OR NOT v_account.is_postable OR v_account.is_subledger_control THEN
            RAISE EXCEPTION 'account % cannot hold an opening balance (inactive, not postable or the subledger control account)', v_account.code
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
    -- The live opening entry of this account or contact: an opening entry that has not been reversed yet.
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
           AND l.account_id = v_target AND ((p_contact_kind = 'CUSTOMER' AND l.customer_id = p_contact_id)
               OR (p_contact_kind = 'VENDOR' AND l.vendor_id = p_contact_id)
               OR (p_contact_kind = 'STAFF' AND l.staff_id = p_contact_id)
               OR (p_contact_id IS NULL AND l.customer_id IS NULL AND l.vendor_id IS NULL AND l.staff_id IS NULL));
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
    v_line := acct.add_line(p_company, v_entry, p_entry_date, v_line, v_store, v_target, p_contact_kind, p_contact_id, p_debit, p_credit, 'Opening balance');
    v_line := acct.add_line(p_company, v_entry, p_entry_date, v_line, v_store, v_equity, NULL, NULL, p_credit, p_debit, 'Opening balance equity');
    RETURN v_entry;
END $function$
;

CREATE OR REPLACE FUNCTION acct.post_voucher(p_company uuid, p_store uuid, p_type text, p_date date, p_debit_account uuid, p_debit_contact_kind text, p_debit_contact_id uuid, p_credit_account uuid, p_credit_contact_kind text, p_credit_contact_id uuid, p_amount core.money, p_payment_mode text, p_ref_no text, p_description text, p_client_transaction uuid, p_allocations jsonb DEFAULT NULL::jsonb)
 RETURNS uuid
 LANGUAGE plpgsql
AS $function$
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
    v_debit  := acct.resolve_voucher_side(p_company, p_debit_account, p_debit_contact_kind, p_debit_contact_id, 'debit');
    v_credit := acct.resolve_voucher_side(p_company, p_credit_account, p_credit_contact_kind, p_credit_contact_id, 'credit');
    IF v_debit = v_credit THEN
        RAISE EXCEPTION 'debit and credit must be different (two contacts cannot be posted against each other: use an account in between)'
            USING ERRCODE = 'check_violation';
    END IF;
    v_date := COALESCE(p_date, CURRENT_DATE);
    v_number := core.next_number(p_company, p_store, p_type || '_VOUCHER');
    v_narration := initcap(lower(p_type)) || ' voucher ' || v_number
                   || COALESCE(': ' || NULLIF(btrim(p_description), ''), '');
    INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
    VALUES (p_company, v_date, p_store, 'VOUCHER', v_voucher, v_narration, core.current_user_id())
    RETURNING id INTO v_entry;
    v_line := acct.add_line(p_company, v_entry, v_date, v_line, p_store, v_debit, p_debit_contact_kind, p_debit_contact_id, p_amount, 0, p_description);
    v_line := acct.add_line(p_company, v_entry, v_date, v_line, p_store, v_credit, p_credit_contact_kind, p_credit_contact_id, 0, p_amount, p_description);
    INSERT INTO acct.vouchers (company_id, id, store_id, voucher_type, voucher_no, voucher_date, debit_account_id, credit_account_id,
                               amount, customer_id, vendor_id, staff_id, payment_mode, ref_no, description, journal_entry_id, client_transaction_id, created_by)
    VALUES (p_company, v_voucher, p_store, p_type, v_number, v_date, v_debit, v_credit,
            p_amount,
            CASE WHEN COALESCE(p_debit_contact_kind, p_credit_contact_kind) = 'CUSTOMER' THEN COALESCE(p_debit_contact_id, p_credit_contact_id) END,
            CASE WHEN COALESCE(p_debit_contact_kind, p_credit_contact_kind) = 'VENDOR' THEN COALESCE(p_debit_contact_id, p_credit_contact_id) END,
            CASE WHEN COALESCE(p_debit_contact_kind, p_credit_contact_kind) = 'STAFF' THEN COALESCE(p_debit_contact_id, p_credit_contact_id) END,
            p_payment_mode, NULLIF(btrim(p_ref_no), ''),
            NULLIF(btrim(p_description), ''), v_entry, p_client_transaction, core.current_user_id());
    IF p_allocations IS NOT NULL AND jsonb_typeof(p_allocations) = 'array' AND jsonb_array_length(p_allocations) > 0 THEN
        IF NOT ((p_type = 'PAYMENT' AND p_debit_contact_kind = 'VENDOR' AND p_debit_contact_id IS NOT NULL) OR (p_type = 'RECEIPT' AND p_credit_contact_kind = 'CUSTOMER' AND p_credit_contact_id IS NOT NULL)) THEN
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
                 WHERE b.company_id = p_company AND b.invoice_id = v_invoice AND b.vendor_id = p_debit_contact_id;
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
                   AND i.customer_id = p_credit_contact_id;
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
END $function$
;

CREATE OR REPLACE FUNCTION acct.resolve_voucher_side(p_company uuid, p_account uuid, p_contact_kind text, p_contact_id uuid, p_side text)
 RETURNS uuid
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_control uuid;
    v_account acct.accounts%ROWTYPE;
BEGIN
    IF (p_contact_kind IS NULL) <> (p_contact_id IS NULL) THEN
        RAISE EXCEPTION 'name both the kind and the id of the contact on the % side', p_side USING ERRCODE = 'check_violation';
    END IF;
    IF p_contact_id IS NOT NULL THEN
        PERFORM 1 WHERE acct.contact_exists(p_company, p_contact_kind, p_contact_id);
        IF NOT FOUND THEN
            RAISE EXCEPTION 'contact % was not found (% side)', p_contact_id, p_side USING ERRCODE = 'check_violation';
        END IF;
        v_control := acct.role_account(p_company, 'SUBLEDGER_CONTROL');
        IF p_account IS NOT NULL AND p_account <> v_control THEN
            RAISE EXCEPTION 'a contact on the % side is posted to the subledger control account: do not name another account', p_side
                USING ERRCODE = 'check_violation';
        END IF;
        RETURN v_control;
    END IF;
    IF p_account IS NULL THEN
        RAISE EXCEPTION 'name an account or a contact on the % side', p_side USING ERRCODE = 'check_violation';
    END IF;
    SELECT * INTO v_account FROM acct.accounts a WHERE a.company_id = p_company AND a.id = p_account;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'account % was not found (% side)', p_account, p_side USING ERRCODE = 'check_violation';
    END IF;
    IF NOT v_account.is_active OR NOT v_account.is_postable THEN
        RAISE EXCEPTION 'account % cannot be posted to (inactive or a heading account)', v_account.code USING ERRCODE = 'check_violation';
    END IF;
    IF v_account.is_subledger_control THEN
        RAISE EXCEPTION 'account % is the subledger control account: choose the customer, vendor or staff member instead', v_account.code USING ERRCODE = 'check_violation';
    END IF;
    IF v_account.system_role IN ('STOCK', 'TRANSIT') THEN
        RAISE EXCEPTION 'account % (%) is posted by stock documents, not by vouchers', v_account.code, v_account.system_role
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN v_account.id;
END $function$
;

CREATE OR REPLACE FUNCTION acct.reverse_entry(p_company uuid, p_entry uuid, p_source_type text, p_source_id uuid, p_desc text)
 RETURNS uuid
 LANGUAGE plpgsql
AS $function$
DECLARE
    e     acct.journal_entries%ROWTYPE;
    v_new uuid;
BEGIN
    SELECT * INTO e FROM acct.journal_entries WHERE company_id = p_company AND id = p_entry;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'entry % not found for reversal', p_entry USING ERRCODE = 'no_data_found';
    END IF;
    INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, reversal_of,
                                      description, posted_by)
    VALUES (p_company, CURRENT_DATE, e.store_id, p_source_type, p_source_id, e.id, p_desc, core.current_user_id())
    RETURNING id INTO v_new;
    INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, customer_id, vendor_id, staff_id,
                                    debit, credit, description)
    SELECT company_id, v_new, CURRENT_DATE, line_no, store_id, account_id, customer_id, vendor_id, staff_id, credit, debit,
           'Reversal: ' || COALESCE(description, '')
      FROM acct.journal_lines
     WHERE company_id = p_company AND entry_id = e.id AND entry_date = e.entry_date;
    RETURN v_new;
END $function$
;

CREATE OR REPLACE FUNCTION acct.add_line(p_company uuid, p_entry uuid, p_date date, p_line_no integer, p_store uuid, p_account uuid, p_contact_kind text, p_contact_id uuid, p_debit core.money, p_credit core.money, p_desc text)
 RETURNS integer
 LANGUAGE plpgsql
AS $function$
BEGIN
    IF COALESCE(p_debit, 0) = 0 AND COALESCE(p_credit, 0) = 0 THEN
        RETURN p_line_no;
    END IF;
    IF (p_contact_kind IS NULL) <> (p_contact_id IS NULL) OR p_contact_kind NOT IN ('CUSTOMER', 'VENDOR', 'STAFF') THEN
        RAISE EXCEPTION 'a journal line names a contact by kind (CUSTOMER, VENDOR or STAFF) and id, or neither' USING ERRCODE = 'check_violation';
    END IF;
    INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, customer_id, vendor_id, staff_id,
                                    debit, credit, description)
    VALUES (p_company, p_entry, p_date, p_line_no, p_store, p_account,
            CASE WHEN p_contact_kind = 'CUSTOMER' THEN p_contact_id END,
            CASE WHEN p_contact_kind = 'VENDOR' THEN p_contact_id END,
            CASE WHEN p_contact_kind = 'STAFF' THEN p_contact_id END,
            COALESCE(p_debit, 0), COALESCE(p_credit, 0), p_desc);
    RETURN p_line_no + 1;
END $function$
;

CREATE OR REPLACE FUNCTION acct.apply_line_to_balances()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    INSERT INTO acct.account_month_balances (company_id, store_id, account_id, period_start, debit, credit)
    VALUES (NEW.company_id, NEW.store_id, NEW.account_id,
            date_trunc('month', NEW.entry_date)::date, NEW.debit, NEW.credit)
    ON CONFLICT (company_id, store_id, account_id, period_start)
    DO UPDATE SET debit  = acct.account_month_balances.debit  + EXCLUDED.debit,
                  credit = acct.account_month_balances.credit + EXCLUDED.credit;
    IF NEW.customer_id IS NOT NULL THEN
        INSERT INTO contacts.customer_balances (company_id, customer_id, debit_total, credit_total, last_entry_date)
        VALUES (NEW.company_id, NEW.customer_id, NEW.debit, NEW.credit, NEW.entry_date)
        ON CONFLICT (company_id, customer_id)
        DO UPDATE SET debit_total     = contacts.customer_balances.debit_total  + EXCLUDED.debit_total,
                      credit_total    = contacts.customer_balances.credit_total + EXCLUDED.credit_total,
                      last_entry_date = GREATEST(contacts.customer_balances.last_entry_date, EXCLUDED.last_entry_date),
                      updated_at      = now();
    END IF;
    IF NEW.vendor_id IS NOT NULL THEN
        INSERT INTO contacts.vendor_balances (company_id, vendor_id, debit_total, credit_total, last_entry_date)
        VALUES (NEW.company_id, NEW.vendor_id, NEW.debit, NEW.credit, NEW.entry_date)
        ON CONFLICT (company_id, vendor_id)
        DO UPDATE SET debit_total     = contacts.vendor_balances.debit_total  + EXCLUDED.debit_total,
                      credit_total    = contacts.vendor_balances.credit_total + EXCLUDED.credit_total,
                      last_entry_date = GREATEST(contacts.vendor_balances.last_entry_date, EXCLUDED.last_entry_date),
                      updated_at      = now();
    END IF;
    IF NEW.staff_id IS NOT NULL THEN
        INSERT INTO contacts.staff_balances (company_id, staff_id, debit_total, credit_total, last_entry_date)
        VALUES (NEW.company_id, NEW.staff_id, NEW.debit, NEW.credit, NEW.entry_date)
        ON CONFLICT (company_id, staff_id)
        DO UPDATE SET debit_total     = contacts.staff_balances.debit_total  + EXCLUDED.debit_total,
                      credit_total    = contacts.staff_balances.credit_total + EXCLUDED.credit_total,
                      last_entry_date = GREATEST(contacts.staff_balances.last_entry_date, EXCLUDED.last_entry_date),
                      updated_at      = now();
    END IF;
    RETURN NULL;
END $function$
;

CREATE OR REPLACE FUNCTION inv.post_stock_variance_journal(p_company uuid, p_store uuid, p_date date, p_source_type text, p_source_id uuid, p_description text, p_net_value numeric)
 RETURNS uuid
 LANGUAGE plpgsql
AS $function$
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
        v_no := acct.add_line(p_company, v_entry, p_date, v_no, p_store, v_stock, NULL, NULL, v_value, 0, p_description);
        v_no := acct.add_line(p_company, v_entry, p_date, v_no, p_store, v_loss, NULL, NULL, 0, v_value, p_description);
    ELSE
        v_no := acct.add_line(p_company, v_entry, p_date, v_no, p_store, v_loss, NULL, NULL, v_value, 0, p_description);
        v_no := acct.add_line(p_company, v_entry, p_date, v_no, p_store, v_stock, NULL, NULL, 0, v_value, p_description);
    END IF;
    RETURN v_entry;
END $function$
;

CREATE OR REPLACE FUNCTION acct.default_group_name(p_group_code text)
 RETURNS text
 LANGUAGE sql
 IMMUTABLE
AS $function$
    SELECT CASE p_group_code
        WHEN '101' THEN 'Cash'                          WHEN '102' THEN 'Bank'
        WHEN '103' THEN 'Subledger control'                 WHEN '201' THEN 'Tax payable'
        WHEN '202' THEN 'Salary payable'                WHEN '203' THEN 'Expense payable'
        WHEN '209' THEN 'Suspense'                      WHEN '301' THEN 'Capital'
        WHEN '302' THEN 'Opening balance equity'        WHEN '401' THEN 'Sales'
        WHEN '501' THEN 'Stock'                         WHEN '502' THEN 'Stock on hand'
        WHEN '600' THEN 'Cost of goods sold'            WHEN '601' THEN 'Stock loss'
        WHEN '602' THEN 'Cost of goods sold (store)'    WHEN '603' THEN 'Salary expense'
        WHEN '610' THEN 'Expenses'
        ELSE 'Group ' || p_group_code END
$function$
;

CREATE OR REPLACE FUNCTION acct.seed_default_chart(p_company uuid)
 RETURNS void
 LANGUAGE plpgsql
AS $function$
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
    INSERT INTO acct.accounts (company_id, code, main_type, group_code, name, store_id, system_role, is_subledger_control) VALUES
        (p_company, '1010', 'ASSET',     '101', 'Cash in hand (head office)', '00000000-0000-0000-0000-000000000000'::uuid, 'CASH', false),
        (p_company, '1020', 'ASSET',     '102', 'Bank',                       '00000000-0000-0000-0000-000000000000'::uuid, 'BANK', false),
        (p_company, '1030', 'ASSET',     '103', 'Subledger control (customers, vendors, staff)', '00000000-0000-0000-0000-000000000000'::uuid, 'SUBLEDGER_CONTROL', true),
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
END $function$
;

CREATE OR REPLACE FUNCTION hr.accrue_line_commission()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_cand     record;
    v_plan     hr.commission_plans%ROWTYPE;
    v_rule     hr.commission_rules%ROWTYPE;
    v_base     numeric;
    v_comm     numeric;
    v_calc     text;
    v_rate     numeric;
    v_fixed    numeric;
    v_min      numeric;
BEGIN
    IF NEW.doc_type = 'RETURN' THEN
        INSERT INTO hr.commission_entries (company_id, store_id, sale_date, invoice_id, invoice_line_id, line_sale_date,
            staff_id, plan_id, rule_id, entry_type, reverses_entry_id, calc_type, rate, fixed_amount,
            qty, base_amount, commission_amount, status)
        SELECT e.company_id, NEW.store_id, NEW.sale_date, NEW.invoice_id, NEW.id, NEW.sale_date,
               e.staff_id, e.plan_id, e.rule_id, 'REVERSAL', e.id, e.calc_type, e.rate, e.fixed_amount,
               -e.qty, -e.base_amount, -e.commission_amount, 'PENDING'
          FROM hr.commission_entries e
         WHERE e.company_id = NEW.company_id
           AND e.invoice_line_id = NEW.original_line_id AND e.line_sale_date = NEW.original_line_sale_date
           AND e.entry_type = 'EARN' AND e.status <> 'CANCELLED'
           AND NOT EXISTS (SELECT 1 FROM hr.commission_entries r
                            WHERE r.company_id = e.company_id AND r.reverses_entry_id = e.id);
        RETURN NULL;
    END IF;
    FOR v_cand IN
        SELECT NEW.salesman_staff_id AS emp, 'SALESMAN'::text AS role
         WHERE NEW.salesman_staff_id IS NOT NULL
        UNION ALL
        SELECT sp.id, 'MANAGER'::text
          FROM contacts.staff sp
         WHERE sp.company_id = NEW.company_id AND sp.is_manager AND sp.current_store_id = NEW.store_id
           AND (sp.left_on IS NULL OR sp.left_on >= NEW.sale_date)
    LOOP
        SELECT p.* INTO v_plan
          FROM hr.commission_plans p
         WHERE p.company_id = NEW.company_id AND p.is_active AND NOT p.is_deleted
           AND p.applies_to = v_cand.role
           AND p.valid_from <= NEW.sale_date AND (p.valid_to IS NULL OR p.valid_to >= NEW.sale_date)
           AND (p.store_id IS NULL OR p.store_id = NEW.store_id)
           AND (NOT EXISTS (SELECT 1 FROM hr.commission_plan_members m
                             WHERE m.company_id = p.company_id AND m.plan_id = p.id)
                OR EXISTS (SELECT 1 FROM hr.commission_plan_members m
                            WHERE m.company_id = p.company_id AND m.plan_id = p.id
                              AND m.staff_id = v_cand.emp))
         ORDER BY EXISTS (SELECT 1 FROM hr.commission_plan_members m
                           WHERE m.company_id = p.company_id AND m.plan_id = p.id) DESC,
                  p.valid_from DESC, p.id DESC
         LIMIT 1;
        IF NOT FOUND THEN
            CONTINUE;
        END IF;
        SELECT r.* INTO v_rule
          FROM hr.commission_rules r
         WHERE r.company_id = NEW.company_id AND r.plan_id = v_plan.id
           AND r.valid_from <= NEW.sale_date AND (r.valid_to IS NULL OR r.valid_to >= NEW.sale_date)
           AND ((r.rule_scope = 'PRODUCT'    AND r.product_id = NEW.product_id)
             OR (r.rule_scope = 'CATEGORY'   AND r.category_id = NEW.category_id)
             OR (r.rule_scope = 'PRICE_BAND' AND r.price_band_id = NEW.price_band_id))
         ORDER BY CASE r.rule_scope WHEN 'PRODUCT' THEN 1 WHEN 'CATEGORY' THEN 2 ELSE 3 END,
                  r.priority DESC, r.id
         LIMIT 1;
        IF v_plan.basis = 'GROSS_MARGIN' THEN
            v_base := NEW.net_amount - NEW.cost_amount;
        ELSE
            v_base := NEW.net_amount;
        END IF;
        IF v_rule.id IS NOT NULL THEN
            v_calc := v_rule.calc_type; v_rate := v_rule.rate; v_fixed := v_rule.fixed_amount;
            v_min  := v_rule.min_line_amount;
        ELSE
            v_calc := v_plan.calc_type; v_rate := v_plan.default_rate; v_fixed := v_plan.default_fixed;
            v_min  := 0;
        END IF;
        IF v_base < v_min THEN
            CONTINUE;
        END IF;
        IF v_calc = 'PERCENT' THEN
            v_comm := core.round_money(NEW.company_id, v_base * v_rate / 100);
        ELSE
            v_comm := core.round_money(NEW.company_id, NEW.qty * v_fixed);
        END IF;
        INSERT INTO hr.commission_entries (company_id, store_id, sale_date, invoice_id, invoice_line_id, line_sale_date,
            staff_id, plan_id, rule_id, entry_type, calc_type, rate, fixed_amount,
            qty, base_amount, commission_amount, status)
        VALUES (NEW.company_id, NEW.store_id, NEW.sale_date, NEW.invoice_id, NEW.id, NEW.sale_date,
            v_cand.emp, v_plan.id, v_rule.id, 'EARN', v_calc,
            CASE WHEN v_calc = 'PERCENT' THEN v_rate END,
            CASE WHEN v_calc = 'FIXED_PER_PIECE' THEN v_fixed END,
            NEW.qty, v_base, v_comm, 'PENDING');
    END LOOP;
    RETURN NULL;
END $function$
;

CREATE OR REPLACE FUNCTION hr.apply_salary_payment()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
DECLARE
    l hr.payroll_lines%ROWTYPE;
BEGIN
    SELECT * INTO l FROM hr.payroll_lines WHERE company_id = NEW.company_id AND id = NEW.payroll_line_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'payroll line % not found', NEW.payroll_line_id;
    END IF;
    IF l.status <> 'APPROVED' THEN
        RAISE EXCEPTION 'salary can be paid only after the payroll is approved (line % is %)', l.id, l.status
            USING ERRCODE = 'check_violation';
    END IF;
    IF NEW.staff_id <> l.staff_id THEN
        RAISE EXCEPTION 'payment employee % does not match payroll line %', NEW.staff_id, l.id
            USING ERRCODE = 'check_violation';
    END IF;
    IF NEW.amount > l.outstanding_amount THEN
        RAISE EXCEPTION 'payment % exceeds the outstanding salary % of line %', NEW.amount, l.outstanding_amount, l.id
            USING ERRCODE = 'check_violation';
    END IF;
    UPDATE hr.payroll_lines
       SET paid_amount = paid_amount + NEW.amount,
           status = CASE WHEN l.outstanding_amount - NEW.amount = 0 THEN 'PAID' ELSE 'APPROVED' END
     WHERE company_id = l.company_id AND id = l.id;
    IF l.outstanding_amount - NEW.amount = 0 THEN
        UPDATE hr.commission_entries SET status = 'PAID'
         WHERE company_id = l.company_id AND payroll_line_id = l.id AND status = 'APPROVED';
    END IF;
    RETURN NEW;
END $function$
;

CREATE OR REPLACE FUNCTION hr.build_payroll_run(p_company uuid, p_store uuid, p_month date, p_user uuid)
 RETURNS uuid
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_run  hr.payroll_runs%ROWTYPE;
    v_from date := date_trunc('month', p_month)::date;
    v_to   date := (date_trunc('month', p_month) + interval '1 month - 1 day')::date;
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'PAYROLL_PREPARE');
    INSERT INTO hr.payroll_runs (company_id, store_id, period_month, created_by)
    VALUES (p_company, p_store, v_from, p_user)
    ON CONFLICT (company_id, store_id, period_month) DO NOTHING;
    SELECT * INTO v_run FROM hr.payroll_runs
     WHERE company_id = p_company AND store_id = p_store AND period_month = v_from FOR UPDATE;
    IF v_run.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'payroll for % is %, only a DRAFT can be rebuilt', v_from, v_run.status
            USING ERRCODE = 'check_violation';
    END IF;
    -- Release the previous draft: cancel its lines and free the rows reserved by them.
    UPDATE hr.commission_entries SET payroll_line_id = NULL
     WHERE company_id = p_company AND payroll_line_id IN
           (SELECT id FROM hr.payroll_lines WHERE company_id = p_company AND payroll_run_id = v_run.id);
    UPDATE hr.payroll_adjustments SET payroll_line_id = NULL
     WHERE company_id = p_company AND payroll_line_id IN
           (SELECT id FROM hr.payroll_lines WHERE company_id = p_company AND payroll_run_id = v_run.id);
    UPDATE hr.payroll_lines SET status = 'CANCELLED'
     WHERE company_id = p_company AND payroll_run_id = v_run.id AND status <> 'CANCELLED';
    INSERT INTO hr.payroll_lines (company_id, payroll_run_id, store_id, staff_id, period_month,
        base_salary, total_sales, commission_amount, incentive_amount, deduction_amount, status, created_by)
    SELECT sp.company_id, v_run.id, p_store, sp.id, v_from,
        COALESCE((SELECT t.base_salary FROM hr.salary_terms t
                   WHERE t.company_id = sp.company_id AND t.staff_id = sp.id
                     AND t.effective_from <= v_to ORDER BY t.effective_from DESC LIMIT 1), sp.base_salary),
        COALESCE((SELECT SUM(l.net_amount) FROM sales.invoice_lines l
                   WHERE l.company_id = sp.company_id AND l.salesman_staff_id = sp.id
                     AND l.sale_date BETWEEN v_from AND v_to), 0),
        COALESCE((SELECT SUM(e.commission_amount) FROM hr.commission_entries e
                   WHERE e.company_id = sp.company_id AND e.staff_id = sp.id
                     AND e.status = 'APPROVED' AND e.payroll_line_id IS NULL
                     AND e.sale_date BETWEEN v_from AND v_to), 0),
        COALESCE((SELECT SUM(a.amount) FROM hr.payroll_adjustments a
                   WHERE a.company_id = sp.company_id AND a.staff_id = sp.id
                     AND a.period_month = v_from AND a.adjustment_type = 'INCENTIVE'
                     AND a.payroll_line_id IS NULL), 0),
        COALESCE((SELECT SUM(a.amount) FROM hr.payroll_adjustments a
                   WHERE a.company_id = sp.company_id AND a.staff_id = sp.id
                     AND a.period_month = v_from AND a.adjustment_type IN ('DEDUCTION','ADVANCE')
                     AND a.payroll_line_id IS NULL), 0),
        'DRAFT', p_user
      FROM contacts.staff sp
     WHERE sp.company_id = p_company AND sp.current_store_id = p_store
       AND (sp.left_on IS NULL OR sp.left_on >= v_from)
       AND (sp.joined_on IS NULL OR sp.joined_on <= v_to);
    -- Reserve the commission and adjustments that went into the new lines.
    UPDATE hr.commission_entries e SET payroll_line_id = pl.id
      FROM hr.payroll_lines pl
     WHERE pl.company_id = p_company AND pl.payroll_run_id = v_run.id AND pl.status = 'DRAFT'
       AND e.company_id = p_company AND e.staff_id = pl.staff_id
       AND e.status = 'APPROVED' AND e.payroll_line_id IS NULL
       AND e.sale_date BETWEEN v_from AND v_to;
    UPDATE hr.payroll_adjustments a SET payroll_line_id = pl.id
      FROM hr.payroll_lines pl
     WHERE pl.company_id = p_company AND pl.payroll_run_id = v_run.id AND pl.status = 'DRAFT'
       AND a.company_id = p_company AND a.staff_id = pl.staff_id
       AND a.period_month = v_from AND a.payroll_line_id IS NULL;
    RETURN v_run.id;
END $function$
;

CREATE OR REPLACE FUNCTION hr.log_salary_term()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_today date;
BEGIN
    IF TG_OP = 'UPDATE' AND NEW.base_salary = OLD.base_salary AND NEW.pay_frequency = OLD.pay_frequency THEN
        RETURN NULL;
    END IF;
    SELECT (now() AT TIME ZONE c.timezone)::date INTO v_today
      FROM platform.companies c WHERE c.id = NEW.company_id;
    INSERT INTO hr.salary_terms (company_id, staff_id, effective_from, base_salary, pay_frequency, changed_by)
    VALUES (NEW.company_id, NEW.id, v_today, NEW.base_salary, NEW.pay_frequency, core.current_user_id())
    ON CONFLICT (company_id, staff_id, effective_from) DO UPDATE
        SET base_salary = EXCLUDED.base_salary, pay_frequency = EXCLUDED.pay_frequency,
            changed_by = EXCLUDED.changed_by, changed_at = now();
    RETURN NULL;
END $function$
;

CREATE OR REPLACE FUNCTION hr.pay_salary(p_company uuid, p_line uuid, p_amount numeric, p_method text, p_paid_on date, p_user uuid, p_reference text DEFAULT NULL::text)
 RETURNS uuid
 LANGUAGE plpgsql
AS $function$
DECLARE
    l       hr.payroll_lines%ROWTYPE;
    v_cash  uuid;
    v_pay   uuid;
    v_entry uuid;
    v_id    uuid;
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'PAYROLL_APPROVE');
    SELECT * INTO l FROM hr.payroll_lines WHERE company_id = p_company AND id = p_line;
    IF NOT FOUND THEN RAISE EXCEPTION 'payroll line % not found', p_line; END IF;
    IF p_method = 'CASH' THEN
        SELECT id INTO v_cash FROM acct.accounts WHERE company_id = p_company AND store_id = l.store_id AND system_role = 'CASH';
    ELSE
        SELECT id INTO v_cash FROM acct.accounts WHERE company_id = p_company AND store_id = '00000000-0000-0000-0000-000000000000'::uuid AND system_role = 'BANK';
    END IF;
    SELECT id INTO v_pay FROM acct.accounts WHERE company_id = p_company AND store_id = '00000000-0000-0000-0000-000000000000'::uuid AND system_role = 'SALARY_PAYABLE';
    INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, description, posted_by)
    VALUES (p_company, p_paid_on, l.store_id, 'SALARY_PAYMENT', 'Salary paid to employee ' || l.staff_id, p_user)
    RETURNING id INTO v_entry;
    INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, staff_id, debit, credit)
    VALUES (p_company, v_entry, p_paid_on, 1, '00000000-0000-0000-0000-000000000000'::uuid, v_pay, l.staff_id, core.round_money(p_company, p_amount), 0),
           (p_company, v_entry, p_paid_on, 2, l.store_id, v_cash, NULL, 0, core.round_money(p_company, p_amount));
    INSERT INTO hr.salary_payments (company_id, store_id, payroll_line_id, staff_id, paid_on, amount,
        method, reference, journal_entry_id, paid_by)
    VALUES (p_company, l.store_id, p_line, l.staff_id, p_paid_on, core.round_money(p_company, p_amount),
        p_method, p_reference, v_entry, p_user)
    RETURNING id INTO v_id;
    RETURN v_id;
END $function$
;

CREATE OR REPLACE FUNCTION hr.reverse_invoice_commission()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    INSERT INTO hr.commission_entries (company_id, store_id, sale_date, invoice_id, invoice_line_id, line_sale_date,
        staff_id, plan_id, rule_id, entry_type, reverses_entry_id, calc_type, rate, fixed_amount,
        qty, base_amount, commission_amount, status)
    SELECT e.company_id, e.store_id, NEW.sale_date, e.invoice_id, e.invoice_line_id, e.line_sale_date,
           e.staff_id, e.plan_id, e.rule_id, 'REVERSAL', e.id, e.calc_type, e.rate, e.fixed_amount,
           -e.qty, -e.base_amount, -e.commission_amount, 'PENDING'
      FROM hr.commission_entries e
     WHERE e.company_id = NEW.company_id AND e.invoice_id = NEW.id
       AND e.entry_type = 'EARN' AND e.status <> 'CANCELLED'
       AND NOT EXISTS (SELECT 1 FROM hr.commission_entries r
                        WHERE r.company_id = e.company_id AND r.reverses_entry_id = e.id);
    RETURN NULL;
END $function$
;

CREATE OR REPLACE FUNCTION platform.on_company_created()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    PERFORM set_config('app.company_id', NEW.id::text, true);
    PERFORM set_config('app.all_stores', 'on', true);
    INSERT INTO core.stores (company_id, id, code, name, is_head_office)
    VALUES (NEW.id, '00000000-0000-0000-0000-000000000000'::uuid, 'HO', 'Head Office', true);
    INSERT INTO integ.channels (company_id, code, name, kind, is_active) VALUES
        (NEW.id, 'POS',     'Store POS', 'STORE_POS', true),
        (NEW.id, 'SHOPIFY', 'Shopify',   'ONLINE',    false);
    PERFORM acct.seed_default_chart(NEW.id);
    INSERT INTO contacts.customers (company_id, code, name)
    VALUES (NEW.id, 'WALKIN', 'Cash Sale');
    RETURN NULL;
END $function$
;

CREATE OR REPLACE FUNCTION purch.check_vendor_article()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM contacts.vendors p
                    WHERE p.company_id = NEW.company_id AND p.id = NEW.vendor_id
                      AND NOT p.is_deleted) THEN
        RAISE EXCEPTION 'vendor % is not an active vendor', NEW.vendor_id USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $function$
;

CREATE OR REPLACE FUNCTION purch.check_vendor_credit()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_limit core.money;
    v_open  numeric;
BEGIN
    SELECT credit_limit INTO v_limit FROM contacts.vendors
     WHERE company_id = NEW.company_id AND id = NEW.vendor_id;
    IF v_limit IS NULL THEN
        RETURN NEW;
    END IF;
    SELECT COALESCE(SUM(GREATEST(vb.remaining_amount, 0)), 0) INTO v_open
      FROM purch.invoices i
      JOIN purch.v_invoice_balance vb ON vb.company_id = i.company_id AND vb.invoice_id = i.id
     WHERE i.company_id = NEW.company_id AND i.vendor_id = NEW.vendor_id
       AND i.id IS DISTINCT FROM NEW.id;
    IF v_open + NEW.net_amount > v_limit THEN
        RAISE EXCEPTION 'vendor credit limit % exceeded: open bills %, this bill %',
            v_limit, v_open, NEW.net_amount USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $function$
;

CREATE OR REPLACE FUNCTION purch.post_invoice_journal()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
DECLARE
    h       purch.invoices%ROWTYPE;
    v_lines int;
    v_gross core.money;
    v_entry uuid;
    v_no    int := 1;
    v_amt   core.money;
BEGIN
    SELECT * INTO h FROM purch.invoices WHERE company_id = NEW.company_id AND id = NEW.id;
    IF h.status <> 'POSTED' OR h.journal_entry_id IS NOT NULL THEN
        RETURN NULL;
    END IF;
    SELECT count(*), COALESCE(sum(line_amount), 0) INTO v_lines, v_gross
      FROM purch.invoice_lines WHERE company_id = h.company_id AND invoice_id = h.id;
    IF v_lines = 0 THEN
        RAISE EXCEPTION 'purchase invoice % has no lines', h.id USING ERRCODE = 'check_violation';
    END IF;
    IF v_gross <> h.gross_amount OR h.gross_amount - h.discount_amount + h.tax_amount <> h.net_amount THEN
        RAISE EXCEPTION 'purchase invoice % totals do not match its lines (gross %/%, net %)',
            h.invoice_no, h.gross_amount, v_gross, h.net_amount USING ERRCODE = 'check_violation';
    END IF;
    IF h.doc_type = 'PURCHASE' THEN
        IF h.net_amount < 0 THEN
            RAISE EXCEPTION 'purchase invoice % has a negative net amount', h.invoice_no USING ERRCODE = 'check_violation';
        END IF;
        v_amt := h.net_amount;
        INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
        VALUES (h.company_id, h.bill_date, h.store_id, 'PURCHASE', h.id, 'Purchase bill ' || h.invoice_no, h.created_by)
        RETURNING id INTO v_entry;
        v_no := acct.add_line(h.company_id, v_entry, h.bill_date, v_no, h.store_id, acct.role_account(h.company_id, 'STOCK'), NULL, NULL, v_amt, 0, 'Stock received');
        v_no := acct.add_line(h.company_id, v_entry, h.bill_date, v_no, h.store_id, acct.role_account(h.company_id, 'SUBLEDGER_CONTROL'), 'VENDOR', h.vendor_id, 0, v_amt, 'Vendor payable');
    ELSIF h.doc_type = 'RETURN' THEN
        v_amt := -h.net_amount;
        INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
        VALUES (h.company_id, h.bill_date, h.store_id, 'PURCHASE_RETURN', h.id, 'Purchase return ' || h.invoice_no, h.created_by)
        RETURNING id INTO v_entry;
        v_no := acct.add_line(h.company_id, v_entry, h.bill_date, v_no, h.store_id, acct.role_account(h.company_id, 'SUBLEDGER_CONTROL'), 'VENDOR', h.vendor_id, v_amt, 0, 'Vendor debited');
        v_no := acct.add_line(h.company_id, v_entry, h.bill_date, v_no, h.store_id, acct.role_account(h.company_id, 'STOCK'), NULL, NULL, 0, v_amt, 'Stock returned to vendor');
    END IF;
    UPDATE purch.invoices SET journal_entry_id = v_entry WHERE company_id = h.company_id AND id = h.id;
    RETURN NULL;
END $function$
;

CREATE OR REPLACE FUNCTION sec.request_password_reset(p_company uuid, p_username text, p_ip inet DEFAULT NULL::inet)
 RETURNS TABLE(out_user_id uuid, out_reset_token text, out_delivery_phone text)
 LANGUAGE plpgsql
AS $function$
DECLARE
    u   sec.users%ROWTYPE;
    pol sec.auth_policy%ROWTYPE;
    v_recent int;
    v_token  text;
BEGIN
    PERFORM set_config('app.company_id', p_company::text, true);
    SELECT * INTO pol FROM sec.auth_policy WHERE company_id = p_company;
    SELECT * INTO u FROM sec.users
     WHERE company_id = p_company AND lower(username) = lower(p_username) AND NOT is_deleted;
    IF NOT FOUND OR u.status = 'DISABLED' THEN
        RETURN;
    END IF;
    SELECT count(*) INTO v_recent FROM sec.password_reset_tokens
     WHERE company_id = p_company AND user_id = u.id AND created_at > now() - interval '1 hour';
    IF v_recent >= pol.reset_max_per_hour THEN
        INSERT INTO sec.login_attempts (company_id, user_id, outcome, ip) VALUES (p_company, u.id, 'RESET_THROTTLED', p_ip);
        RETURN;
    END IF;
    v_token := sec.new_token();
    INSERT INTO sec.password_reset_tokens (company_id, user_id, token_hash, expires_at, requested_ip)
    VALUES (p_company, u.id, sec.token_hash(v_token), now() + make_interval(mins => pol.reset_token_minutes), p_ip);
    INSERT INTO sec.login_attempts (company_id, user_id, outcome, ip) VALUES (p_company, u.id, 'RESET_REQUESTED', p_ip);
    RETURN QUERY
        SELECT u.id, v_token, pa.phone
          FROM (SELECT 1) AS one
          LEFT JOIN contacts.staff pa ON pa.company_id = p_company AND pa.id = u.staff_id;
END $function$
;

CREATE OR REPLACE FUNCTION sales.post_exchange(p_company uuid, p_store uuid, p_date date, p_client_transaction uuid, p_customer uuid, p_salesman uuid, p_walkin_name text, p_walkin_mobile text, p_device uuid, p_description text, p_return_lines jsonb, p_sale_lines jsonb, p_payments jsonb, p_change core.money)
 RETURNS TABLE(return_invoice_id uuid, sale_invoice_id uuid)
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_walkin   uuid;
    v_customer uuid;
    v_return   uuid;
    v_sale     uuid;
    v_return_net bigint;
    v_sale_credit bigint;
BEGIN
    SELECT id INTO v_walkin FROM contacts.customers WHERE company_id = p_company AND code = 'WALKIN' AND NOT is_deleted;
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
END $function$
;

CREATE OR REPLACE FUNCTION sales.post_return_invoice(p_company uuid, p_store uuid, p_sale_date date, p_client_transaction uuid, p_customer uuid, p_description text, p_lines jsonb, p_device uuid DEFAULT NULL::uuid, p_keep_walkin_customer boolean DEFAULT false)
 RETURNS uuid
 LANGUAGE plpgsql
AS $function$
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
    SELECT id INTO v_walkin FROM contacts.customers WHERE company_id = p_company AND code = 'WALKIN' AND NOT is_deleted;
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
               i.customer_id AS invoice_customer
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
            'size_group_id', o.size_group_id, 'product_type_id', o.product_type_id, 'salesman_staff_id', o.salesman_staff_id,
            'price_band_id', o.price_band_id, 'qty', -v_qty, 'list_price', o.list_price, 'unit_price', o.unit_price,
            'discount_source', o.discount_source, 'rule_id', o.rule_id, 'conflict_group_id', o.conflict_group_id, 'bundle_id', o.bundle_id,
            'discount_amount', v_gross - v_net, 'net_amount', v_net, 'unit_cost', o.unit_cost, 'cost_amount', v_cost,
            'original_line_id', o.id, 'original_line_sale_date', o.sale_date,
            'reason', NULLIF(btrim(e.elem ->> 'reason'), ''), 'item_condition', v_condition);
    END LOOP;
    -- the credit goes to the chosen customer, else to the customer of the sale; "Cash Sale" (the walk-in) is no account to credit
    v_customer := COALESCE(p_customer, v_first_customer);
    IF v_customer IS NOT NULL THEN
        PERFORM 1 FROM contacts.customers WHERE company_id = p_company AND id = v_customer AND NOT is_deleted AND status = 'ACTIVE';
        IF NOT FOUND THEN
            RAISE EXCEPTION 'the customer was not found or is not an active customer' USING ERRCODE = 'check_violation';
        END IF;
        IF v_customer = v_walkin AND NOT p_keep_walkin_customer THEN
            v_customer := NULL;
        END IF;
    END IF;
    v_invoice_no := core.next_number(p_company, p_store, 'SALE_RETURN_INVOICE');
    INSERT INTO sales.invoices (company_id, id, store_id, channel_id, doc_type, invoice_no, sale_date, device_id, device_sale_uuid, occurred_at,
                                original_invoice_id, customer_id, salesman_staff_id,
                                gross_amount, line_discount_amount, bill_discount_amount, taxable_amount, tax_amount, net_amount,
                                paid_amount, change_amount, credit_amount, cost_amount, created_by)
    VALUES (p_company, v_invoice, p_store, v_channel, 'RETURN', v_invoice_no, p_sale_date, p_device, p_client_transaction, now(),
            v_first_original, v_customer,
            (SELECT salesman_staff_id FROM sales.invoices WHERE company_id = p_company AND id = v_first_original),
            t_gross, t_gross - t_net, 0, t_net, 0, t_net,
            0, 0, 0, t_cost, core.current_user_id());
    INSERT INTO sales.invoice_lines (company_id, id, invoice_id, line_no, store_id, channel_id, sale_date, doc_type, variant_id, product_id,
                                     category_id, department_id, brand_id, season_id, gender_id, color_id, size_group_id, product_type_id,
                                     salesman_staff_id, price_band_id, qty, list_price, unit_price, discount_source, rule_id, conflict_group_id, bundle_id,
                                     discount_amount, tax_rate, taxable_amount, tax_amount, net_amount, unit_cost, cost_amount,
                                     original_line_id, original_line_sale_date)
    SELECT p_company, r.id, v_invoice, r.line_no, p_store, v_channel, p_sale_date, 'RETURN', r.variant_id, r.product_id,
           r.category_id, r.department_id, r.brand_id, r.season_id, r.gender_id, r.color_id, r.size_group_id, r.product_type_id,
           r.salesman_staff_id, r.price_band_id, r.qty, r.list_price, r.unit_price, r.discount_source, r.rule_id, r.conflict_group_id, r.bundle_id,
           r.discount_amount, 0, r.net_amount, 0, r.net_amount, r.unit_cost, r.cost_amount,
           r.original_line_id, r.original_line_sale_date
      FROM jsonb_to_recordset(v_calculated) AS r(line_no int, id uuid, variant_id uuid, product_id uuid, category_id uuid, department_id uuid,
                                                  brand_id uuid, season_id uuid, gender_id uuid, color_id uuid, size_group_id uuid, product_type_id uuid,
                                                  salesman_staff_id uuid, price_band_id uuid, qty numeric, list_price bigint, unit_price bigint,
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
END $function$
;

CREATE OR REPLACE FUNCTION sales.post_invoice_journal()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
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
        IF v_credit > 0 AND h.customer_id IS NULL THEN
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
            v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'CASH'), NULL, NULL, v_cash, 0, 'Cash received');
        ELSE
            IF v_pay_total <> v_cash THEN
                RAISE EXCEPTION 'invoice % payments add up to % but % was received (paid % less change %)',
                    h.invoice_no, v_pay_total, v_cash, h.paid_amount, h.change_amount USING ERRCODE = 'check_violation';
            END IF;
            FOR v_pay IN SELECT method, payment_account_id, amount FROM sales.payments
                          WHERE company_id = h.company_id AND invoice_id = h.id ORDER BY received_at, id
            LOOP
                v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, v_pay.payment_account_id, NULL, NULL, v_pay.amount, 0,
                                      'Received (' || v_pay.method || ')');
            END LOOP;
        END IF;
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'SUBLEDGER_CONTROL'), 'CUSTOMER', h.customer_id, v_credit, 0, 'Customer balance');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'SALES'), NULL, NULL, 0, v_amt, 'Sales');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'TAX_PAYABLE'), NULL, NULL, 0, v_tx, 'Tax');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'COGS'), NULL, NULL, v_k, 0, 'Cost of goods sold');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'STOCK'), NULL, NULL, 0, v_k, 'Stock issued');
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
        v_customer := h.customer_id;
        INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
        VALUES (h.company_id, h.sale_date, h.store_id, 'SALE_RETURN', h.id, 'Return invoice ' || h.invoice_no, h.created_by)
        RETURNING id INTO v_entry;
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'SALES'), NULL, NULL, v_amt, 0, 'Sales returned');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'TAX_PAYABLE'), NULL, NULL, v_tx, 0, 'Tax reversed');
        IF v_customer IS NOT NULL THEN
            v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'SUBLEDGER_CONTROL'), 'CUSTOMER', v_customer, 0, v_credit, 'Customer credited');
        ELSE
            v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'SUSPENSE'), NULL, NULL, 0, v_credit, 'Return without customer: accountant to clear');
        END IF;
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'STOCK'), NULL, NULL, v_k, 0, 'Stock returned');
        v_no := acct.add_line(h.company_id, v_entry, h.sale_date, v_no, h.store_id, acct.role_account(h.company_id, 'COGS'), NULL, NULL, 0, v_k, 'Cost of goods reversed');
    END IF;
    UPDATE sales.invoices SET journal_entry_id = v_entry WHERE company_id = h.company_id AND id = h.id;
    RETURN NULL;
END $function$
;

CREATE OR REPLACE FUNCTION sales.post_sale_invoice(p_company uuid, p_store uuid, p_sale_date date, p_client_transaction uuid, p_customer uuid, p_salesman uuid, p_walkin_name text, p_walkin_mobile text, p_device uuid, p_description text, p_lines jsonb, p_payments jsonb, p_change core.money, p_price_change_permission text, p_discount_permission text, p_replaces_invoice uuid DEFAULT NULL::uuid, p_replace_reason text DEFAULT NULL::text, p_allow_walkin_credit boolean DEFAULT false)
 RETURNS uuid
 LANGUAGE plpgsql
AS $function$
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
    SELECT id INTO v_walkin FROM contacts.customers WHERE company_id = p_company AND code = 'WALKIN' AND NOT is_deleted;
    IF p_customer IS NOT NULL THEN
        PERFORM 1 FROM contacts.customers WHERE company_id = p_company AND id = p_customer AND NOT is_deleted AND status = 'ACTIVE';
        IF NOT FOUND THEN
            RAISE EXCEPTION 'the customer was not found or is not an active customer' USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF p_salesman IS NOT NULL THEN
        PERFORM 1 FROM contacts.staff WHERE company_id = p_company AND id = p_salesman AND NOT is_deleted AND status = 'ACTIVE';
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
             WHERE a.company_id = p_company AND a.id = (p.elem ->> 'paymentAccountId')::uuid AND a.is_active AND a.is_postable AND NOT a.is_subledger_control
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
                                occurred_at, customer_id, salesman_staff_id, walkin_name, walkin_mobile,
                                gross_amount, line_discount_amount, bill_discount_amount, taxable_amount, tax_amount, net_amount,
                                paid_amount, change_amount, credit_amount, cost_amount, created_by)
    VALUES (p_company, v_invoice, p_store, v_channel, 'SALE', v_invoice_no, p_sale_date, v_daily_bill, p_device, p_client_transaction,
            now(), p_customer, p_salesman, NULLIF(btrim(p_walkin_name), ''), NULLIF(btrim(p_walkin_mobile), ''),
            t_gross, t_discount, 0, t_net, 0, t_net,
            v_paid, COALESCE(p_change, 0), v_credit, t_cost, core.current_user_id());
    INSERT INTO sales.invoice_lines (company_id, id, invoice_id, line_no, store_id, channel_id, sale_date, doc_type, variant_id, product_id,
                                     category_id, department_id, brand_id, season_id, gender_id, color_id, size_group_id, product_type_id,
                                     salesman_staff_id, qty, list_price, unit_price, discount_source, rule_id, conflict_group_id,
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
END $function$
;

CREATE OR REPLACE FUNCTION rpt.account_ledger(p_company uuid, p_account uuid, p_from date, p_to date, p_store uuid DEFAULT NULL::uuid)
 RETURNS TABLE(out_entry_date date, out_entry_id uuid, out_source_type text, out_description text, out_contact_kind text, out_contact_id uuid, out_debit core.money, out_credit core.money, out_running core.money)
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_ACCOUNTS');
    RETURN QUERY
    WITH opening AS (
        SELECT COALESCE(SUM(l.debit - l.credit), 0) AS bal
          FROM acct.journal_lines l
         WHERE l.company_id = p_company AND l.account_id = p_account AND l.entry_date < p_from
           AND (p_store IS NULL OR l.store_id = p_store)
    ),
    moves AS (
        SELECT l.entry_date, l.entry_id, l.line_no, e.source_type,
               COALESCE(l.description, e.description) AS descr,
               CASE WHEN l.customer_id IS NOT NULL THEN 'CUSTOMER' WHEN l.vendor_id IS NOT NULL THEN 'VENDOR' WHEN l.staff_id IS NOT NULL THEN 'STAFF' END AS contact_kind,
               COALESCE(l.customer_id, l.vendor_id, l.staff_id) AS contact_id, l.debit, l.credit
          FROM acct.journal_lines l
          JOIN acct.journal_entries e ON e.company_id = l.company_id AND e.id = l.entry_id AND e.entry_date = l.entry_date
         WHERE l.company_id = p_company AND l.account_id = p_account
           AND l.entry_date BETWEEN p_from AND p_to
           AND (p_store IS NULL OR l.store_id = p_store)
    )
    SELECT m.entry_date, m.entry_id, m.source_type, m.descr, m.contact_kind, m.contact_id,
           m.debit::core.money, m.credit::core.money,
           ((SELECT bal FROM opening) + SUM(m.debit - m.credit) OVER (ORDER BY m.entry_date, m.entry_id, m.line_no))::core.money
      FROM moves m
     ORDER BY m.entry_date, m.entry_id, m.line_no;
END $function$
;

CREATE OR REPLACE FUNCTION rpt.commission_accrual(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL::uuid)
 RETURNS TABLE(out_employee_id uuid, out_employee text, out_status text, out_earned core.money, out_reversed core.money, out_net core.money, out_entries bigint)
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT c.staff_id, p.name, c.status,
           COALESCE(SUM(c.commission_amount) FILTER (WHERE c.entry_type = 'EARN'), 0)::core.money,
           COALESCE(SUM(c.commission_amount) FILTER (WHERE c.entry_type = 'REVERSAL'), 0)::core.money,
           SUM(c.commission_amount)::core.money,
           COUNT(*)
      FROM hr.commission_entries c
      JOIN contacts.staff p ON p.company_id = c.company_id AND p.id = c.staff_id
     WHERE c.company_id = p_company AND c.sale_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR c.store_id = p_store)
     GROUP BY c.staff_id, p.name, c.status
     ORDER BY p.name, c.status;
END $function$
;

CREATE OR REPLACE FUNCTION rpt.customer_balances(p_company uuid)
 RETURNS TABLE(out_customer_id uuid, out_customer text, out_balance core.money, out_last_entry date)
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT b.customer_id, p.name, b.balance::core.money, b.last_entry_date
      FROM contacts.customer_balances b
      JOIN contacts.customers p ON p.company_id = b.company_id AND p.id = b.customer_id
     WHERE b.company_id = p_company AND b.balance <> 0
     ORDER BY p.name;
END $function$
;

CREATE OR REPLACE FUNCTION rpt.general_journal(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL::uuid)
 RETURNS TABLE(out_entry_id uuid, out_entry_date date, out_source_type text, out_source_id uuid, out_line_no integer, out_account_code text, out_account_name text, out_contact_name text, out_debit core.money, out_credit core.money, out_description text)
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_ACCOUNTS');
    RETURN QUERY
    SELECT e.id, e.entry_date, e.source_type, e.source_id, l.line_no, a.code, a.name, COALESCE(cu.name, ve.name, st.name),
           l.debit, l.credit, COALESCE(l.description, e.description)
      FROM acct.journal_entries e
      JOIN acct.journal_lines l ON l.company_id = e.company_id AND l.entry_id = e.id AND l.entry_date = e.entry_date
      JOIN acct.accounts a ON a.company_id = l.company_id AND a.id = l.account_id
      LEFT JOIN contacts.customers cu ON cu.company_id = l.company_id AND cu.id = l.customer_id
      LEFT JOIN contacts.vendors ve ON ve.company_id = l.company_id AND ve.id = l.vendor_id
      LEFT JOIN contacts.staff st ON st.company_id = l.company_id AND st.id = l.staff_id
     WHERE e.company_id = p_company AND e.entry_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR e.store_id = p_store)
     ORDER BY e.entry_date, e.id, l.line_no;
END $function$
;

CREATE OR REPLACE FUNCTION rpt.purchase_register(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL::uuid, p_vendor uuid DEFAULT NULL::uuid)
 RETURNS TABLE(out_invoice_id uuid, out_invoice_no bigint, out_doc_type text, out_bill_date date, out_store_id uuid, out_vendor text, out_vendor_bill_no text, out_gross core.money, out_discount core.money, out_tax core.money, out_net core.money, out_status text)
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT i.id, i.invoice_no, i.doc_type, i.bill_date, i.store_id, p.name, i.vendor_bill_no,
           i.gross_amount, i.discount_amount, i.tax_amount, i.net_amount, i.status
      FROM purch.invoices i
      JOIN contacts.vendors p ON p.company_id = i.company_id AND p.id = i.vendor_id
     WHERE i.company_id = p_company AND i.bill_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR i.store_id = p_store)
       AND (p_vendor IS NULL OR i.vendor_id = p_vendor)
     ORDER BY i.bill_date, i.invoice_no;
END $function$
;

CREATE OR REPLACE FUNCTION rpt.sale_register(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL::uuid, p_doc_type text DEFAULT NULL::text, p_customer uuid DEFAULT NULL::uuid)
 RETURNS TABLE(out_invoice_id uuid, out_invoice_no bigint, out_doc_type text, out_sale_date date, out_store_id uuid, out_customer text, out_qty core.qty, out_gross core.money, out_discount core.money, out_net core.money, out_tax core.money, out_cost core.money, out_profit core.money, out_status text)
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT h.id, h.invoice_no, h.doc_type, h.sale_date, h.store_id,
           COALESCE(pa.name, h.walkin_name),
           COALESCE(q.qty, 0)::core.qty,
           h.gross_amount, (h.line_discount_amount + h.bill_discount_amount)::core.money,
           h.net_amount, h.tax_amount, h.cost_amount,
           (h.net_amount - h.cost_amount)::core.money, h.status
      FROM sales.invoices h
      LEFT JOIN contacts.customers pa ON pa.company_id = h.company_id AND pa.id = h.customer_id
      LEFT JOIN (SELECT l.invoice_id, SUM(l.qty) AS qty
                   FROM sales.invoice_lines l
                  WHERE l.company_id = p_company AND l.sale_date BETWEEN p_from AND p_to
                  GROUP BY l.invoice_id) q ON q.invoice_id = h.id
     WHERE h.company_id = p_company AND h.sale_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR h.store_id = p_store)
       AND (p_doc_type IS NULL OR h.doc_type = p_doc_type)
       AND (p_customer IS NULL OR h.customer_id = p_customer)
     ORDER BY h.sale_date, h.invoice_no;
END $function$
;

CREATE OR REPLACE FUNCTION rpt.vendor_aging(p_company uuid, p_as_of date)
 RETURNS TABLE(out_vendor_id uuid, out_vendor text, out_not_due core.money, out_days_1_30 core.money, out_days_31_60 core.money, out_days_61_90 core.money, out_over_90 core.money, out_total core.money)
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT v.vendor_id, p.name,
           COALESCE(SUM(v.remaining_amount) FILTER (WHERE v.due_date IS NULL OR v.due_date >= p_as_of), 0)::core.money,
           COALESCE(SUM(v.remaining_amount) FILTER (WHERE p_as_of - v.due_date BETWEEN 1 AND 30), 0)::core.money,
           COALESCE(SUM(v.remaining_amount) FILTER (WHERE p_as_of - v.due_date BETWEEN 31 AND 60), 0)::core.money,
           COALESCE(SUM(v.remaining_amount) FILTER (WHERE p_as_of - v.due_date BETWEEN 61 AND 90), 0)::core.money,
           COALESCE(SUM(v.remaining_amount) FILTER (WHERE p_as_of - v.due_date > 90), 0)::core.money,
           SUM(v.remaining_amount)::core.money
      FROM purch.v_invoice_balance v
      JOIN contacts.vendors p ON p.company_id = v.company_id AND p.id = v.vendor_id
     WHERE v.company_id = p_company AND v.bill_date <= p_as_of AND v.remaining_amount <> 0
     GROUP BY v.vendor_id, p.name
     ORDER BY p.name;
END $function$
;


-- 9. Views again, on the new tables and columns
CREATE VIEW acct.v_live_opening_balances WITH (security_invoker = true) AS
SELECT e.company_id,
    e.id AS entry_id,
    e.entry_date,
    e.store_id,
    e.source_id,
    l.account_id,
    l.customer_id,
    l.vendor_id,
    l.staff_id,
    l.debit,
    l.credit
   FROM ((acct.journal_entries e
     JOIN acct.journal_lines l ON (((l.company_id = e.company_id) AND (l.entry_id = e.id) AND (l.entry_date = e.entry_date))))
     JOIN acct.accounts a ON (((a.company_id = l.company_id) AND (a.id = l.account_id) AND (a.system_role IS DISTINCT FROM 'OPENING_EQUITY'::text))))
  WHERE ((e.source_type = 'OPENING'::text) AND (e.reversal_of IS NULL) AND (NOT (EXISTS ( SELECT 1
           FROM acct.journal_entries r
          WHERE ((r.company_id = e.company_id) AND (r.reversal_of = e.id))))));

CREATE VIEW hr.v_commission_summary WITH (security_invoker = true) AS
SELECT company_id,
    store_id,
    staff_id,
    (date_trunc('month'::text, (sale_date)::timestamp with time zone))::date AS period_month,
    COALESCE(sum((base_amount)::bigint) FILTER (WHERE (status <> 'CANCELLED'::text)), (0)::numeric) AS eligible_base,
    COALESCE(sum((commission_amount)::bigint) FILTER (WHERE (status <> 'CANCELLED'::text)), (0)::numeric) AS earned,
    COALESCE(sum((commission_amount)::bigint) FILTER (WHERE (status = 'PENDING'::text)), (0)::numeric) AS pending,
    COALESCE(sum((commission_amount)::bigint) FILTER (WHERE (status = ANY (ARRAY['APPROVED'::text, 'ON_HOLD'::text]))), (0)::numeric) AS approved,
    COALESCE(sum((commission_amount)::bigint) FILTER (WHERE (status = 'PAID'::text)), (0)::numeric) AS paid
   FROM hr.commission_entries e
  GROUP BY company_id, store_id, staff_id, ((date_trunc('month'::text, (sale_date)::timestamp with time zone))::date);

CREATE VIEW hr.v_salary_outstanding WITH (security_invoker = true) AS
SELECT l.company_id,
    l.store_id,
    l.period_month,
    l.staff_id,
    p.name AS employee_name,
    l.base_salary,
    l.commission_amount,
    l.incentive_amount,
    l.deduction_amount,
    l.net_pay,
    l.paid_amount,
    l.outstanding_amount,
    l.status
   FROM (hr.payroll_lines l
     JOIN contacts.staff p ON (((p.company_id = l.company_id) AND (p.id = l.staff_id))))
  WHERE (l.status <> 'CANCELLED'::text);

CREATE VIEW purch.v_invoice_balance WITH (security_invoker = true) AS
SELECT i.company_id,
    i.id AS invoice_id,
    i.store_id,
    i.vendor_id,
    i.bill_date,
    i.due_date,
    i.net_amount,
    COALESCE(a.paid, (0)::numeric) AS paid_amount,
    ((i.net_amount)::numeric - COALESCE(a.paid, (0)::numeric)) AS remaining_amount
   FROM (purch.invoices i
     LEFT JOIN ( SELECT va.company_id,
            va.purchase_invoice_id,
            sum((va.amount)::bigint) AS paid
           FROM (acct.voucher_allocations va
             JOIN acct.vouchers v ON (((v.company_id = va.company_id) AND (v.id = va.voucher_id) AND (v.status = 'POSTED'::text))))
          WHERE (va.purchase_invoice_id IS NOT NULL)
          GROUP BY va.company_id, va.purchase_invoice_id) a ON (((a.company_id = i.company_id) AND (a.purchase_invoice_id = i.id))))
  WHERE ((i.doc_type = 'PURCHASE'::text) AND (i.status = 'POSTED'::text));

CREATE VIEW purch.v_vendor_due WITH (security_invoker = true) AS
SELECT s.company_id,
    s.store_id,
    s.vendor_id,
    p.name AS vendor_name,
    s.id AS schedule_id,
    s.due_date,
    ((s.planned_amount)::bigint - (s.paid_amount)::bigint) AS amount_due,
        CASE
            WHEN (s.due_date < CURRENT_DATE) THEN 'OVERDUE'::text
            WHEN (s.due_date <= (CURRENT_DATE + 7)) THEN 'DUE_7_DAYS'::text
            ELSE 'UPCOMING'::text
        END AS due_status,
    (CURRENT_DATE - s.due_date) AS days_overdue
   FROM (purch.vendor_payment_schedule s
     JOIN contacts.vendors p ON (((p.company_id = s.company_id) AND (p.id = s.vendor_id))))
  WHERE (s.status = ANY (ARRAY['OPEN'::text, 'PARTIAL'::text]));

CREATE VIEW purch.v_vendor_order_lines WITH (security_invoker = true) AS
SELECT o.company_id,
    o.store_id,
    o.vendor_id,
    o.id AS order_id,
    o.order_no,
    o.order_date,
    l.id AS order_line_id,
    l.variant_id,
    v.product_id,
    l.qty_ordered,
    l.qty_posted AS qty_received,
        CASE
            WHEN (o.status = 'CANCELLED'::text) THEN (0)::numeric
            ELSE ((l.qty_ordered)::numeric - (l.qty_posted)::numeric)
        END AS qty_outstanding,
        CASE
            WHEN (o.status = 'CANCELLED'::text) THEN 'CANCELLED'::text
            WHEN ((l.qty_posted)::numeric = (0)::numeric) THEN 'PENDING'::text
            WHEN ((l.qty_posted)::numeric < (l.qty_ordered)::numeric) THEN 'PARTIAL'::text
            ELSE 'RECEIVED'::text
        END AS line_status
   FROM ((orders.orders o
     JOIN orders.order_lines l ON (((l.company_id = o.company_id) AND (l.order_id = o.id))))
     JOIN catalog.variants v ON (((v.company_id = l.company_id) AND (v.id = l.variant_id))))
  WHERE ((o.order_type = 'VENDOR_PO'::text) AND (NOT o.is_deleted));

CREATE VIEW purch.v_vendor_summary WITH (security_invoker = true) AS
SELECT p.company_id,
    p.id AS vendor_id,
    p.name AS vendor_name,
    (- COALESCE((b.balance)::bigint, (0)::bigint)) AS payable_balance,
    COALESCE(s.total_purchases, (0)::numeric) AS total_purchases,
    COALESCE(s.total_returns, (0)::numeric) AS total_returns,
    COALESCE(s.open_remaining, (0)::numeric) AS open_bill_remaining,
    COALESCE(s.overdue_remaining, (0)::numeric) AS overdue_remaining,
    n.next_due_date,
    n.next_due_amount,
    p.credit_limit,
        CASE
            WHEN (p.credit_limit IS NULL) THEN NULL::numeric
            ELSE ((p.credit_limit)::numeric - COALESCE(s.open_remaining, (0)::numeric))
        END AS credit_available
   FROM (((contacts.vendors p
     LEFT JOIN contacts.vendor_balances b ON (((b.company_id = p.company_id) AND (b.vendor_id = p.id))))
     LEFT JOIN LATERAL ( SELECT sum(
                CASE
                    WHEN (i.doc_type = 'PURCHASE'::text) THEN (i.net_amount)::bigint
                    ELSE (0)::bigint
                END) AS total_purchases,
            sum(
                CASE
                    WHEN (i.doc_type = 'RETURN'::text) THEN (- (i.net_amount)::bigint)
                    ELSE (0)::bigint
                END) AS total_returns,
            sum(GREATEST(vb.remaining_amount, (0)::numeric)) AS open_remaining,
            sum(GREATEST(vb.remaining_amount, (0)::numeric)) FILTER (WHERE (vb.due_date < CURRENT_DATE)) AS overdue_remaining
           FROM (purch.invoices i
             LEFT JOIN purch.v_invoice_balance vb ON (((vb.company_id = i.company_id) AND (vb.invoice_id = i.id))))
          WHERE ((i.company_id = p.company_id) AND (i.vendor_id = p.id) AND (i.status = 'POSTED'::text))) s ON (true))
     LEFT JOIN LATERAL ( SELECT ps.due_date AS next_due_date,
            sum(((ps.planned_amount)::bigint - (ps.paid_amount)::bigint)) AS next_due_amount
           FROM purch.vendor_payment_schedule ps
          WHERE ((ps.company_id = p.company_id) AND (ps.vendor_id = p.id) AND (ps.status = ANY (ARRAY['OPEN'::text, 'PARTIAL'::text])) AND (ps.due_date = ( SELECT min(x.due_date) AS min
                   FROM purch.vendor_payment_schedule x
                  WHERE ((x.company_id = ps.company_id) AND (x.vendor_id = ps.vendor_id) AND (x.status = ANY (ARRAY['OPEN'::text, 'PARTIAL'::text]))))))
          GROUP BY ps.due_date) n ON (true))
  WHERE (NOT p.is_deleted);
GRANT SELECT ON acct.v_live_opening_balances TO pos_app;
GRANT SELECT ON hr.v_salary_outstanding TO pos_app;
GRANT SELECT ON purch.v_invoice_balance TO pos_app;

-- 10. Permissions: the old party screens become customer, vendor and staff screens (grants are copied, then the old rows go)
CREATE TEMP TABLE perm_map (old_screen text, new_module text, new_screen text) ON COMMIT DROP;
INSERT INTO perm_map VALUES
    ('NEW_PARTY', 'CUSTOMERS', 'NEW_CUSTOMER'), ('NEW_PARTY', 'VENDORS', 'NEW_VENDOR'), ('NEW_PARTY', 'HUMAN_RESOURCES', 'NEW_STAFF'),
    ('VIEW_PARTY', 'CUSTOMERS', 'VIEW_CUSTOMERS'), ('VIEW_PARTY', 'VENDORS', 'VIEW_VENDORS'), ('VIEW_PARTY', 'HUMAN_RESOURCES', 'VIEW_STAFF'),
    ('SEARCH_PARTY', 'CUSTOMERS', 'SEARCH_CUSTOMER'), ('SEARCH_PARTY', 'VENDORS', 'SEARCH_VENDOR'), ('SEARCH_PARTY', 'HUMAN_RESOURCES', 'SEARCH_STAFF'),
    ('ACCOUNT_DETAIL', 'CUSTOMERS', 'CUSTOMER_ACCOUNT_DETAIL'), ('ACCOUNT_DETAIL', 'VENDORS', 'VENDOR_ACCOUNT_DETAIL'), ('ACCOUNT_DETAIL', 'HUMAN_RESOURCES', 'STAFF_ACCOUNT_DETAIL'),
    ('PROMISE', 'CUSTOMERS', 'PROMISE'),
    ('CASH_CALCULATOR', 'CASH_MANAGEMENT', 'CASH_CALCULATOR'), ('VIEW_CASH_DETAIL', 'CASH_MANAGEMENT', 'VIEW_CASH_DETAIL'),
    ('NEW_SALARY_SHEET', 'HUMAN_RESOURCES', 'NEW_SALARY_SHEET'), ('VIEW_SALARY_SHEET', 'HUMAN_RESOURCES', 'VIEW_SALARY_SHEET');

INSERT INTO sec.permissions (code, description, module_code, screen_code, action_code, sort_order)
SELECT m.new_module || '.' || m.new_screen || '.' || p.action_code,
       initcap(replace(m.new_screen, '_', ' ')) || ': ' || initcap(p.action_code), m.new_module, m.new_screen, p.action_code, p.sort_order
  FROM sec.permissions p JOIN perm_map m ON p.module_code = 'PARTY' AND p.screen_code = m.old_screen
ON CONFLICT (code) DO NOTHING;

INSERT INTO sec.user_permissions (company_id, user_id, permission_code, granted_by, granted_at)
SELECT up.company_id, up.user_id, m.new_module || '.' || m.new_screen || '.' || split_part(up.permission_code, '.', 3), up.granted_by, up.granted_at
  FROM sec.user_permissions up JOIN perm_map m ON split_part(up.permission_code, '.', 1) = 'PARTY' AND split_part(up.permission_code, '.', 2) = m.old_screen
ON CONFLICT DO NOTHING;
INSERT INTO sec.role_template_permissions (company_id, template_id, permission_code)
SELECT rp.company_id, rp.template_id, m.new_module || '.' || m.new_screen || '.' || split_part(rp.permission_code, '.', 3)
  FROM sec.role_template_permissions rp JOIN perm_map m ON split_part(rp.permission_code, '.', 1) = 'PARTY' AND split_part(rp.permission_code, '.', 2) = m.old_screen
ON CONFLICT DO NOTHING;
DELETE FROM sec.user_permissions WHERE permission_code LIKE 'PARTY.%';
DELETE FROM sec.role_template_permissions WHERE permission_code LIKE 'PARTY.%';
DELETE FROM sec.permissions WHERE module_code = 'PARTY';

-- 11. The party schema goes (no CASCADE: if anything still depends on it the migration stops)
DROP FUNCTION party.next_party_code(uuid);
DROP TABLE party.staff_store_history;
DROP TABLE party.staff_profiles;
DROP TABLE party.party_balances;
DROP TABLE party.party_code_counters;
DROP FUNCTION contacts.classify_legacy(uuid, uuid, text);
DROP TABLE party.parties;
DROP SCHEMA party;

-- 12. Nothing in the catalogue may still carry the old name
DO $$
DECLARE v_left text;
BEGIN
    SELECT string_agg(x, ', ') INTO v_left FROM (
        SELECT 'column ' || c.relnamespace::regnamespace || '.' || c.relname || '.' || a.attname AS x
          FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
         WHERE a.attname ILIKE '%party%' AND NOT a.attisdropped AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
           AND c.relnamespace::regnamespace::text NOT IN ('pg_catalog', 'information_schema')
        UNION ALL
        SELECT 'relation ' || c.relnamespace::regnamespace || '.' || c.relname FROM pg_class c
         WHERE c.relname ILIKE '%party%' AND c.relnamespace::regnamespace::text NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
        UNION ALL
        SELECT 'constraint ' || conrelid::regclass || '.' || conname FROM pg_constraint WHERE conname ILIKE '%party%'
        UNION ALL
        SELECT 'function ' || p.oid::regprocedure FROM pg_proc p
         WHERE p.prokind IN ('f', 'p') AND p.pronamespace::regnamespace::text NOT IN ('pg_catalog', 'information_schema')
           AND (p.proname ILIKE '%party%' OR pg_get_functiondef(p.oid) ILIKE '%party%' OR pg_get_functiondef(p.oid) ILIKE '%parties%')
        UNION ALL
        SELECT 'view ' || c.oid::regclass FROM pg_class c WHERE c.relkind IN ('v', 'm') AND pg_get_viewdef(c.oid) ILIKE '%party%'
         AND c.relnamespace::regnamespace::text NOT IN ('pg_catalog', 'information_schema')
    ) q;
    IF v_left IS NOT NULL THEN
        RAISE EXCEPTION 'objects still named after party: %', v_left;
    END IF;
END $$;
