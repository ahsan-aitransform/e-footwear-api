-- =====================================================================
--  FootwearPOS schema v7: every id is a UUID (PostgreSQL 18 uuidv7(): time-ordered, index friendly).
--  Generated from the v6 schema + platform admin + email changes, then tested. This single file is the baseline;
--  apply it to an EMPTY database as the owner (postgres). Later changes go in db/migrations/ as 0001, 0002, ...
--    psql -h localhost -U postgres -d E-Footwear -v ON_ERROR_STOP=1 -f db/schema_v7_uuid.sql
--
--  Conventions:
--   * Every primary key, foreign key and "created_by"-style user column is uuid.
--   * The head office of a company is a store whose id is the nil uuid 00000000-0000-0000-0000-000000000000
--     (it replaces the old store id 0). "No band", "no rule" in report keys use the same nil uuid.
--   * Document numbers (invoice_no, voucher_no, ...), row_ver and change_seq stay bigint: they are counters, not ids.
--   * Sign-in is by email (unique across companies); the platform administrator uses a username.
-- =====================================================================
BEGIN;
-- ===== FootwearPOS_v6_schema (1).sql =====
-- =====================================================================
--  FootwearPOS  -  Production database schema, version 5.0
--  Target: PostgreSQL 16+ (tested on PostgreSQL 16.15)
--  Install: once, on an empty database, as the schema owner role.
--  Money: whole PKR (core.money = bigint). Quantities: core.qty numeric(14,3).
--  Tenancy: company_id on every business table; store_id on store-level tables.
-- =====================================================================

CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE SCHEMA platform;
CREATE SCHEMA core;
CREATE SCHEMA sec;
CREATE SCHEMA master;
CREATE SCHEMA party;
CREATE SCHEMA catalog;
CREATE SCHEMA integ;
CREATE SCHEMA acct;
CREATE SCHEMA promo;
CREATE SCHEMA inv;
CREATE SCHEMA orders;
CREATE SCHEMA sales;
CREATE SCHEMA purch;
CREATE SCHEMA cash;
CREATE SCHEMA hr;
CREATE SCHEMA exp;
CREATE SCHEMA ops;
CREATE SCHEMA rpt;
CREATE SCHEMA audit;

-- ---------- domains (change here if money ever moves to paisa) ----------
CREATE DOMAIN core.money     AS bigint;           -- whole rupees; rounding via core.round_money
CREATE DOMAIN core.qty       AS numeric(14,3);    -- pieces, kg, metres
CREATE DOMAIN core.unit_cost AS numeric(14,4);    -- average cost keeps 4 decimals internally
CREATE DOMAIN core.pct       AS numeric(7,4)
    CHECK (VALUE >= 0 AND VALUE <= 100);

CREATE SEQUENCE core.change_seq AS bigint;        -- global change cursor for incremental sync

-- ---------- platform ----------
CREATE TABLE platform.plans (
    code          text PRIMARY KEY,
    name          text NOT NULL,
    max_stores    int  NOT NULL CHECK (max_stores > 0),
    max_users     int  NOT NULL CHECK (max_users > 0),
    features      jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE TABLE platform.companies (
    id              uuid NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    name            text NOT NULL,
    legal_name      text,
    ntn             text,
    strn            text,
    timezone        text NOT NULL DEFAULT 'Asia/Karachi',
    currency_code   char(3) NOT NULL DEFAULT 'PKR',
    rounding_mode   text NOT NULL DEFAULT 'FLOOR' CHECK (rounding_mode IN ('FLOOR','NEAREST')),
    price_band_basis text NOT NULL DEFAULT 'LIST' CHECK (price_band_basis IN ('LIST','NET')),
    plan_code       text NOT NULL DEFAULT 'STANDARD' REFERENCES platform.plans(code),
    status          text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','SUSPENDED','CLOSED')),
    invoice_terms   text,                              -- printed on receipts
    settings        jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1
);

-- ---------- session context (set by the application per transaction) ----------
-- app.company_id, app.user_id, app.store_ids ('3,5'), app.all_stores ('on'|'off'), app.store_id
CREATE OR REPLACE FUNCTION core.current_company_id() RETURNS uuid
LANGUAGE sql STABLE AS
$$ SELECT NULLIF(current_setting('app.company_id', true), '')::uuid $$;

CREATE OR REPLACE FUNCTION core.current_user_id() RETURNS uuid
LANGUAGE sql STABLE AS
$$ SELECT NULLIF(current_setting('app.user_id', true), '')::uuid $$;

CREATE OR REPLACE FUNCTION core.store_allowed(p_store uuid) RETURNS boolean
LANGUAGE sql STABLE AS
$$ SELECT COALESCE(current_setting('app.all_stores', true), 'off') = 'on'
       OR p_store = ANY (COALESCE(string_to_array(NULLIF(current_setting('app.store_ids', true), ''), ',')::uuid[],
                                  '{}'::uuid[])) $$;

-- Rounding of every computed customer-facing amount. FLOOR = drop paisa (1039.20 -> 1039).
CREATE OR REPLACE FUNCTION core.round_money(p_company uuid, p_amount numeric) RETURNS core.money
LANGUAGE sql STABLE AS
$$ SELECT CASE WHEN c.rounding_mode = 'NEAREST' THEN round(p_amount)::bigint
               ELSE floor(p_amount)::bigint END
   FROM platform.companies c WHERE c.id = p_company $$;

-- ---------- generic triggers ----------
-- Maintains updated_at / updated_by / row_ver on mutable tables.
CREATE OR REPLACE FUNCTION core.touch_row() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    NEW.updated_at := now();
    NEW.updated_by := core.current_user_id();
    NEW.row_ver    := OLD.row_ver + 1;
    RETURN NEW;
END $$;

-- Refreshes the global change cursor on tables synced to devices.
CREATE OR REPLACE FUNCTION core.bump_change_seq() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    NEW.change_seq := nextval('core.change_seq');
    RETURN NEW;
END $$;

-- Append-only enforcement for ledgers, journals, audit and event logs.
CREATE OR REPLACE FUNCTION core.forbid_update_delete() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION '% on % is not allowed: this table is append-only (post a reversal instead)',
        TG_OP, TG_TABLE_NAME USING ERRCODE = 'insufficient_privilege';
END $$;

-- Generic change log for master data and money-moving rows (row-level, AFTER).
CREATE OR REPLACE FUNCTION audit.log_row_change() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_row     jsonb;
    v_company uuid;
    v_id      text;
BEGIN
    -- Works for any table: uses "id" when present, otherwise the primary-key columns.
    v_row := to_jsonb(CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END);
    v_company := (v_row->>'company_id')::uuid;
    v_id := v_row->>'id';
    IF v_id IS NULL THEN
        SELECT string_agg(v_row->>a.attname, ':' ORDER BY a.attnum) INTO v_id
          FROM pg_index i
          JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
         WHERE i.indrelid = TG_RELID AND i.indisprimary;
    END IF;
    INSERT INTO audit.activity_log (company_id, user_id, store_id, action, entity_type, entity_id,
                                    old_values, new_values)
    VALUES (v_company, core.current_user_id(),
            NULLIF(current_setting('app.store_id', true), '')::uuid,
            TG_OP, TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME, v_id,
            CASE WHEN TG_OP IN ('UPDATE','DELETE') THEN to_jsonb(OLD) - 'password_hash' END,
            CASE WHEN TG_OP IN ('INSERT','UPDATE') THEN to_jsonb(NEW) - 'password_hash' END);
    RETURN NULL;
END $$;

-- Monthly range partitions for the large fact and ledger tables.
CREATE OR REPLACE FUNCTION core.create_monthly_partitions(p_schema text, p_table text,
                                                          p_from date, p_to date) RETURNS int
LANGUAGE plpgsql AS $$
DECLARE
    m date := date_trunc('month', p_from)::date;
    n int := 0;
BEGIN
    WHILE m < p_to LOOP
        EXECUTE format('CREATE TABLE IF NOT EXISTS %I.%I PARTITION OF %I.%I FOR VALUES FROM (%L) TO (%L)',
                       p_schema, p_table || '_' || to_char(m, 'YYYYMM'), p_schema, p_table,
                       m, (m + INTERVAL '1 month')::date);
        m := (m + INTERVAL '1 month')::date;
        n := n + 1;
    END LOOP;
    RETURN n;
END $$;

-- =====================================================================
-- CORE: stores, devices, number series, attachments
-- Store id 0 = HEAD OFFICE pseudo-store (company-level expenses, accounts, owner views).
-- Stock, sales and transfers never use store 0 (CHECK store_id <> '00000000-0000-0000-0000-000000000000'::uuid).
-- =====================================================================
CREATE TABLE core.stores (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    code            text NOT NULL,
    name            text NOT NULL,
    is_head_office  boolean NOT NULL DEFAULT false,
    city_id         uuid,                                  -- FK added after master.cities
    address         text,
    phone           text,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CONSTRAINT stores_code_uq UNIQUE (company_id, code),
    CONSTRAINT stores_head_office_chk CHECK ((is_head_office AND id = '00000000-0000-0000-0000-000000000000'::uuid) OR (NOT is_head_office AND id <> '00000000-0000-0000-0000-000000000000'::uuid))
);

CREATE TABLE core.devices (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    store_id        uuid NOT NULL,
    device_uuid     uuid NOT NULL UNIQUE,
    label           text,
    token_hash      text,
    is_active       boolean NOT NULL DEFAULT true,
    last_seen_at    timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);
-- Several POS terminals per store are allowed (requirement 5.1). Lookup index only.
CREATE INDEX ix_devices_store ON core.devices (company_id, store_id);

-- Server-assigned document numbers. period_key '1900-01-01' = never resets; a date = daily reset.
CREATE TABLE core.number_series (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    store_id        uuid NOT NULL,
    series_code     text NOT NULL,
    period_key      date NOT NULL DEFAULT DATE '1900-01-01',
    next_value      bigint NOT NULL DEFAULT 1 CHECK (next_value > 0),
    PRIMARY KEY (company_id, store_id, series_code, period_key),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id)
);

-- Atomic, row-locking number allocation. Returns 1, 2, 3 ... per series and period.
CREATE OR REPLACE FUNCTION core.next_number(p_company uuid, p_store uuid, p_series text,
                                            p_period date DEFAULT DATE '1900-01-01') RETURNS bigint
LANGUAGE plpgsql AS $$
DECLARE v bigint;
BEGIN
    INSERT INTO core.number_series (company_id, store_id, series_code, period_key, next_value)
    VALUES (p_company, p_store, p_series, p_period, 2)
    ON CONFLICT (company_id, store_id, series_code, period_key)
    DO UPDATE SET next_value = core.number_series.next_value + 1
    RETURNING next_value - 1 INTO v;
    RETURN v;
END $$;

CREATE TABLE core.attachments (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    entity_type     text NOT NULL,                  -- 'voucher','purchase_invoice','expense','claim', ...
    entity_id       uuid NOT NULL,
    storage_key     text NOT NULL,                  -- object-storage key; file itself is not in the DB
    mime_type       text,
    size_bytes      integer CHECK (size_bytes >= 0),
    sha256          char(64),
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, storage_key)
);
CREATE INDEX ix_attachments_entity ON core.attachments (company_id, entity_type, entity_id);

-- =====================================================================
-- MASTER LISTS (company-level)
-- =====================================================================
DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['departments','brands','seasons','genders','colors',
                             'size_groups','product_types','units','locations','cities'] LOOP
        EXECUTE format($f$
            CREATE TABLE master.%I (
                company_id   uuid NOT NULL REFERENCES platform.companies(id),
                id           uuid NOT NULL DEFAULT uuidv7(),
                code         text,
                name         text NOT NULL,
                name_ur      text,
                sort_order   int NOT NULL DEFAULT 0,
                is_active    boolean NOT NULL DEFAULT true,
                is_deleted   boolean NOT NULL DEFAULT false,
                deleted_at   timestamptz,
                deleted_by   uuid,
                created_at   timestamptz NOT NULL DEFAULT now(),
                created_by   uuid,
                updated_at   timestamptz,
                updated_by   uuid,
                row_ver      bigint NOT NULL DEFAULT 1,
                PRIMARY KEY (company_id, id)
            )$f$, t);
        EXECUTE format('CREATE UNIQUE INDEX %I ON master.%I (company_id, lower(name)) WHERE NOT is_deleted',
                       'ux_' || t || '_name', t);
        EXECUTE format('CREATE TRIGGER trg_touch BEFORE UPDATE ON master.%I FOR EACH ROW EXECUTE FUNCTION core.touch_row()', t);
    END LOOP;
END $$;

-- Cities: name is unique case-insensitively; the application warns on near-duplicates (Shiekhupura / Sheikhpura).

CREATE TABLE master.categories (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    department_id   uuid NOT NULL,
    parent_id       uuid,
    code            text,
    name            text NOT NULL,
    name_ur         text,
    sort_order      int NOT NULL DEFAULT 0,
    is_active       boolean NOT NULL DEFAULT true,
    is_deleted      boolean NOT NULL DEFAULT false,
    deleted_at      timestamptz,
    deleted_by      uuid,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, department_id) REFERENCES master.departments(company_id, id),
    FOREIGN KEY (company_id, parent_id) REFERENCES master.categories(company_id, id),
    CHECK (parent_id IS NULL OR parent_id <> id)
);
CREATE UNIQUE INDEX ux_categories_name ON master.categories (company_id, department_id, lower(name)) WHERE NOT is_deleted;
CREATE TRIGGER trg_touch BEFORE UPDATE ON master.categories FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE master.sizes (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    size_group_id   uuid NOT NULL,
    size_code       text NOT NULL,
    sort_order      int NOT NULL DEFAULT 0,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, size_group_id, size_code),
    FOREIGN KEY (company_id, size_group_id) REFERENCES master.size_groups(company_id, id)
);

ALTER TABLE core.stores ADD CONSTRAINT stores_city_fk FOREIGN KEY (company_id, city_id)
    REFERENCES master.cities(company_id, id);

-- =====================================================================
-- PARTY: customers, vendors and staff in one table
-- =====================================================================
CREATE TABLE party.parties (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    code            text NOT NULL,
    name            text NOT NULL,
    name_ur         text,
    father_name     text,
    cnic            text CHECK (cnic IS NULL OR cnic ~ '^[0-9]{5}-[0-9]{7}-[0-9]$'),
    phone_primary   text,
    phone_secondary text,
    email           text,
    address         text,
    city_id         uuid,
    is_customer     boolean NOT NULL DEFAULT false,
    is_vendor       boolean NOT NULL DEFAULT false,
    is_staff        boolean NOT NULL DEFAULT false,
    credit_limit    core.money CHECK (credit_limit IS NULL OR credit_limit >= 0),   -- vendors: max open bills (NULL = no limit), set by admin; enforced on posting
    status          text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','INACTIVE')),
    attributes      jsonb NOT NULL DEFAULT '{}'::jsonb,
    change_seq      bigint NOT NULL DEFAULT nextval('core.change_seq'),
    is_deleted      boolean NOT NULL DEFAULT false,
    deleted_at      timestamptz,
    deleted_by      uuid,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CONSTRAINT parties_has_role_chk CHECK (is_customer OR is_vendor OR is_staff),
    FOREIGN KEY (company_id, city_id) REFERENCES master.cities(company_id, id)
);
CREATE UNIQUE INDEX ux_parties_code  ON party.parties (company_id, code) WHERE NOT is_deleted;
CREATE UNIQUE INDEX ux_parties_cnic  ON party.parties (company_id, cnic) WHERE cnic IS NOT NULL AND NOT is_deleted;
CREATE INDEX ix_parties_name  ON party.parties (company_id, lower(name)) WHERE NOT is_deleted;
CREATE INDEX ix_parties_phone ON party.parties (company_id, phone_primary) WHERE phone_primary IS NOT NULL;
CREATE INDEX ix_parties_roles ON party.parties (company_id, is_vendor) WHERE is_vendor AND NOT is_deleted;
CREATE TRIGGER trg_touch BEFORE UPDATE ON party.parties FOR EACH ROW EXECUTE FUNCTION core.touch_row();
CREATE TRIGGER trg_seq   BEFORE UPDATE ON party.parties FOR EACH ROW EXECUTE FUNCTION core.bump_change_seq();

CREATE TABLE party.staff_profiles (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    party_id            uuid NOT NULL,
    designation         text,
    is_manager          boolean NOT NULL DEFAULT false,
    base_salary         core.money NOT NULL DEFAULT 0 CHECK (base_salary >= 0),
    pay_frequency       text NOT NULL DEFAULT 'MONTHLY' CHECK (pay_frequency IN ('MONTHLY','BIWEEKLY','WEEKLY')),
    current_store_id    uuid NOT NULL,
    joined_on           date,
    left_on             date,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, party_id),
    FOREIGN KEY (company_id, party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, current_store_id) REFERENCES core.stores(company_id, id),
    CHECK (current_store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON party.staff_profiles FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE party.staff_store_history (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    party_id        uuid NOT NULL,
    from_store_id   uuid,
    to_store_id     uuid NOT NULL,
    effective_on    date NOT NULL,
    changed_by      uuid,
    changed_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, party_id) REFERENCES party.staff_profiles(company_id, party_id),
    FOREIGN KEY (company_id, to_store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, from_store_id) REFERENCES core.stores(company_id, id),
    CHECK (from_store_id IS NULL OR from_store_id <> to_store_id)
);
CREATE INDEX ix_staff_history ON party.staff_store_history (company_id, party_id, effective_on);

-- Running balance per party. balance > 0 : party owes us (customer). balance < 0 : we owe (vendor).
-- Maintained by a trigger on acct.journal_lines (see section ACCOUNTING).
CREATE TABLE party.party_balances (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    party_id        uuid NOT NULL,
    debit_total     core.money NOT NULL DEFAULT 0,
    credit_total    core.money NOT NULL DEFAULT 0,
    balance         core.money GENERATED ALWAYS AS (debit_total - credit_total) STORED,
    last_entry_date date,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    change_seq      bigint NOT NULL DEFAULT nextval('core.change_seq'),
    PRIMARY KEY (company_id, party_id),
    FOREIGN KEY (company_id, party_id) REFERENCES party.parties(company_id, id)
);
CREATE TRIGGER trg_seq BEFORE UPDATE ON party.party_balances FOR EACH ROW EXECUTE FUNCTION core.bump_change_seq();

-- =====================================================================
-- SECURITY: users, roles, permissions
-- =====================================================================
CREATE TABLE sec.roles (
    code    text PRIMARY KEY,
    name    text NOT NULL
);
INSERT INTO sec.roles (code, name) VALUES
    ('COMPANY_OWNER',    'Company Owner'),
    ('STORE_MANAGER',    'Store Manager'),
    ('CASHIER_SALESMAN', 'Cashier / Salesman'),
    ('ACCOUNTANT',       'Accountant'),
    ('PLATFORM_ADMIN',   'Platform Admin');

CREATE TABLE sec.permissions (
    code        text PRIMARY KEY,
    description text NOT NULL
);
INSERT INTO sec.permissions (code, description) VALUES
    ('SALE_CREATE','Create sales'), ('SALE_RETURN','Process returns and exchanges'),
    ('PRICE_CHANGE','Manual price change on a sale'), ('DISCOUNT_MANUAL','Manual discount on a sale'),
    ('DISCOUNT_APPROVE','Approve manual price or discount'), ('PROMO_MANAGE','Create and end promotions'),
    ('PROMO_CONFLICT_RESOLVE','Choose which overlapping discount applies'),
    ('PURCHASE_CREATE','Record purchases'), ('PURCHASE_PAY','Pay vendors'),
    ('STOCK_ADJUST','Stock adjustments'), ('STOCK_AUDIT','Stock audits'),
    ('TRANSFER_CREATE','Create and ship transfers'), ('TRANSFER_RECEIVE','Receive transfers'),
    ('TRANSFER_LOSS_DECIDE','Assign or write off transfer losses'),
    ('VOUCHER_POST','Post vouchers'), ('VOUCHER_DELETE','Void vouchers'),
    ('EXPENSE_RECORD','Record expenses'), ('EXPENSE_APPROVE','Approve expenses'),
    ('PAYROLL_PREPARE','Prepare salary sheets'), ('PAYROLL_APPROVE','Approve and pay salaries'),
    ('COMMISSION_VIEW','View commissions'), ('CASH_COUNT','End-of-day cash count'),
    ('CLAIM_APPROVE','Approve product claims'), ('OPENING_BALANCE','Enter opening balances'),
    ('REPORT_VIEW','View reports'), ('REPORT_BUILDER','Build and share custom reports'),
    ('MASTER_EDIT','Edit master lists and products'), ('USER_ADMIN','Manage users and roles'),
    ('SHOPIFY_ADMIN','Connect and manage Shopify'),
    ('COMMISSION_APPROVE','Approve commissions'), ('CLAIM_REQUEST','Raise product claims'),
    ('VENDOR_MANAGE','Manage vendors and vendor articles');

CREATE TABLE sec.users (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    username        text NOT NULL,
    password_hash   text NOT NULL,                  -- argon2id, produced by the application
    full_name       text NOT NULL,
    role_code       text NOT NULL REFERENCES sec.roles(code),
    staff_party_id  uuid,                          -- FK added after party.parties (below)
    all_stores      boolean NOT NULL DEFAULT false,  -- owner: sees every store of the company
    status          text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','LOCKED','DISABLED')),
    failed_logins   int NOT NULL DEFAULT 0 CHECK (failed_logins >= 0),
    must_change_password boolean NOT NULL DEFAULT true,
    password_changed_at  timestamptz,
    locked_until    timestamptz,
    last_login_at   timestamptz,
    is_deleted      boolean NOT NULL DEFAULT false,
    deleted_at      timestamptz,
    deleted_by      uuid,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, staff_party_id) REFERENCES party.parties(company_id, id)
);
CREATE UNIQUE INDEX ux_users_username ON sec.users (company_id, lower(username)) WHERE NOT is_deleted;
CREATE TRIGGER trg_touch BEFORE UPDATE ON sec.users FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE sec.user_stores (
    company_id  uuid NOT NULL REFERENCES platform.companies(id),
    user_id     uuid NOT NULL,
    store_id    uuid NOT NULL,
    PRIMARY KEY (company_id, user_id, store_id),
    FOREIGN KEY (company_id, user_id) REFERENCES sec.users(company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);

CREATE TABLE sec.user_permissions (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    user_id         uuid NOT NULL,
    permission_code text NOT NULL REFERENCES sec.permissions(code),
    granted_by      uuid,
    granted_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, user_id, permission_code),
    FOREIGN KEY (company_id, user_id) REFERENCES sec.users(company_id, id)
);

-- =====================================================================
-- INTEGRATION: sales channels (POS, SHOPIFY), external ids, sync queues
-- Shopify IDs live only here. No business table has Shopify columns.
-- =====================================================================
CREATE TABLE integ.channels (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    code            text NOT NULL,                 -- 'POS', 'SHOPIFY', later others
    name            text NOT NULL,
    kind            text NOT NULL CHECK (kind IN ('STORE_POS','ONLINE')),
    is_active       boolean NOT NULL DEFAULT true,
    settings_enc    bytea,                         -- API credentials, encrypted by the application
    external_shop   text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, code)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON integ.channels FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE integ.channel_prices (                -- optional online price per variant
    company_id  uuid NOT NULL REFERENCES platform.companies(id),
    channel_id  uuid NOT NULL,
    variant_id  uuid NOT NULL,
    price       core.money NOT NULL CHECK (price >= 0),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    updated_by  uuid,
    PRIMARY KEY (company_id, channel_id, variant_id),
    FOREIGN KEY (company_id, channel_id) REFERENCES integ.channels(company_id, id)
);

CREATE TABLE integ.location_map (                  -- store <-> Shopify location, publish threshold
    company_id              uuid NOT NULL REFERENCES platform.companies(id),
    channel_id              uuid NOT NULL,
    store_id                uuid NOT NULL,
    external_location_id    text NOT NULL,
    safety_buffer           core.qty NOT NULL DEFAULT 0 CHECK (safety_buffer >= 0),
    publish_min_qty         core.qty NOT NULL DEFAULT 3 CHECK (publish_min_qty >= 0),   -- hide below this
    is_active               boolean NOT NULL DEFAULT true,
    PRIMARY KEY (company_id, channel_id, store_id),
    UNIQUE (company_id, channel_id, external_location_id),
    FOREIGN KEY (company_id, channel_id) REFERENCES integ.channels(company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);

CREATE TABLE integ.external_ids (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    channel_id      uuid NOT NULL,
    entity_type     text NOT NULL,                 -- product, variant, inventory_item, location, customer, order, refund
    local_id        uuid NOT NULL,
    external_id     text NOT NULL,
    sync_status     text NOT NULL DEFAULT 'OK' CHECK (sync_status IN ('OK','PENDING','ERROR')),
    last_synced_at  timestamptz,
    PRIMARY KEY (company_id, channel_id, entity_type, local_id),
    UNIQUE (company_id, channel_id, entity_type, external_id),
    FOREIGN KEY (company_id, channel_id) REFERENCES integ.channels(company_id, id)
);

-- Stock held for unfulfilled online orders. Available = on hand - HELD reservations.
CREATE TABLE integ.stock_reservations (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    channel_id          uuid NOT NULL,
    store_id            uuid NOT NULL,
    variant_id          uuid NOT NULL,
    qty                 core.qty NOT NULL CHECK (qty > 0),
    external_order_id   text NOT NULL,
    status              text NOT NULL DEFAULT 'HELD' CHECK (status IN ('HELD','RELEASED','FULFILLED')),
    created_at          timestamptz NOT NULL DEFAULT now(),
    closed_at           timestamptz,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, channel_id) REFERENCES integ.channels(company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id)
);  -- variant FK added in the deferred-FK section (catalog is created later)

CREATE TABLE integ.sync_outbox (                   -- written in the same transaction as the business change
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    channel_id      uuid NOT NULL,
    entity_type     text NOT NULL,
    local_id        uuid NOT NULL,
    payload         jsonb NOT NULL,
    status          text NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','SENT','FAILED','DEAD')),
    attempts        int NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    last_error      text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    sent_at         timestamptz,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, channel_id) REFERENCES integ.channels(company_id, id)
);
CREATE INDEX ix_outbox_pending ON integ.sync_outbox (company_id, created_at) WHERE status IN ('PENDING','FAILED');

CREATE TABLE integ.webhook_inbox (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    channel_id      uuid NOT NULL,
    webhook_id      text NOT NULL,                 -- unique per channel: processed once
    topic           text NOT NULL,
    raw_payload     jsonb NOT NULL,
    received_at     timestamptz NOT NULL DEFAULT now(),
    processed_at    timestamptz,
    error           text,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, channel_id, webhook_id),
    FOREIGN KEY (company_id, channel_id) REFERENCES integ.channels(company_id, id)
);
CREATE INDEX ix_webhook_unprocessed ON integ.webhook_inbox (company_id, received_at) WHERE processed_at IS NULL;

CREATE TABLE integ.sync_log (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    channel_id      uuid,
    kind            text NOT NULL,                 -- ERROR, RECON_DIFF, FIX, INFO
    details         jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id)
);

-- =====================================================================
-- ACCOUNTING: chart of accounts, periods, double-entry journal, vouchers
-- Store 0 = head office. Store accounts exist per store (created by trigger).
-- =====================================================================
CREATE TABLE acct.accounts (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    code            text NOT NULL,
    main_type       text NOT NULL CHECK (main_type IN ('ASSET','LIABILITY','CAPITAL','INCOME','STOCK','EXPENSE')),
    group_code      text NOT NULL,
    name            text NOT NULL,
    parent_id       uuid,
    store_id        uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'::uuid,      -- 0 = head office account
    system_role     text,                           -- CASH, BANK, PARTY_CONTROL, STOCK, TRANSIT, STOCK_LOSS, SALES, COGS, ...
    is_postable     boolean NOT NULL DEFAULT true,
    is_party_control boolean NOT NULL DEFAULT false,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, code),
    FOREIGN KEY (company_id, parent_id) REFERENCES acct.accounts(company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id)
);
CREATE UNIQUE INDEX ux_accounts_system_role ON acct.accounts (company_id, store_id, system_role) WHERE system_role IS NOT NULL;
CREATE TRIGGER trg_touch BEFORE UPDATE ON acct.accounts FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE acct.periods (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    period_start    date NOT NULL CHECK (EXTRACT(DAY FROM period_start) = 1),
    status          text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','CLOSED')),
    closed_by       uuid,
    closed_at       timestamptz,
    PRIMARY KEY (company_id, period_start)
);

CREATE TABLE acct.journal_entries (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    entry_uuid      uuid NOT NULL DEFAULT gen_random_uuid(),
    entry_date      date NOT NULL,
    store_id        uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'::uuid,
    source_type     text NOT NULL,                  -- SALE, SALE_VOID, PURCHASE, VOUCHER, PAYROLL, EXPENSE, TRANSFER, LOSS, REVERSAL, OPENING, ...
    source_id       uuid,
    reversal_of     uuid,
    description     text,
    posted_at       timestamptz NOT NULL DEFAULT now(),
    posted_by       uuid,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, id, entry_date),            -- target of the partitioned journal_lines FK
    UNIQUE (company_id, entry_uuid),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, reversal_of) REFERENCES acct.journal_entries(company_id, id)
);
CREATE UNIQUE INDEX ux_je_one_reversal ON acct.journal_entries (company_id, reversal_of) WHERE reversal_of IS NOT NULL;
CREATE INDEX ix_je_source ON acct.journal_entries (company_id, source_type, source_id);
CREATE INDEX ix_je_store_date ON acct.journal_entries (company_id, store_id, entry_date);

CREATE TABLE acct.journal_lines (
    company_id      uuid NOT NULL,
    id              uuid NOT NULL DEFAULT uuidv7(),
    entry_id        uuid NOT NULL,
    entry_date      date NOT NULL,
    line_no         int NOT NULL,
    store_id        uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'::uuid,
    account_id      uuid NOT NULL,
    party_id        uuid,
    debit           core.money NOT NULL DEFAULT 0 CHECK (debit >= 0),
    credit          core.money NOT NULL DEFAULT 0 CHECK (credit >= 0),
    description     text,
    PRIMARY KEY (company_id, id, entry_date),
    UNIQUE (company_id, entry_id, line_no, entry_date),
    CHECK ((debit = 0) <> (credit = 0)),            -- exactly one side is non-zero
    FOREIGN KEY (company_id, entry_id, entry_date) REFERENCES acct.journal_entries(company_id, id, entry_date),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, account_id) REFERENCES acct.accounts(company_id, id),
    FOREIGN KEY (company_id, party_id) REFERENCES party.parties(company_id, id)
) PARTITION BY RANGE (entry_date);
CREATE INDEX ix_jl_account ON acct.journal_lines (company_id, account_id, entry_date);
CREATE INDEX ix_jl_party   ON acct.journal_lines (company_id, party_id, entry_date) WHERE party_id IS NOT NULL;
CREATE INDEX ix_jl_entry   ON acct.journal_lines (company_id, entry_id, entry_date);

-- Month-end and live account totals. Trial balance and P&L read this table, not the raw lines.
CREATE TABLE acct.account_month_balances (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    store_id        uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'::uuid,
    account_id      uuid NOT NULL,
    period_start    date NOT NULL,
    debit           core.money NOT NULL DEFAULT 0,
    credit          core.money NOT NULL DEFAULT 0,
    PRIMARY KEY (company_id, store_id, account_id, period_start),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, account_id) REFERENCES acct.accounts(company_id, id)
);

CREATE TABLE acct.vouchers (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    voucher_type        text NOT NULL CHECK (voucher_type IN ('PAYMENT','RECEIPT','EXPENSE','JOURNAL')),
    voucher_no          bigint NOT NULL,
    voucher_date        date NOT NULL,
    debit_account_id    uuid NOT NULL,
    credit_account_id   uuid NOT NULL,
    amount              core.money NOT NULL CHECK (amount > 0),
    party_id            uuid,
    payment_mode        text CHECK (payment_mode IN ('CASH','BANK','CHEQUE','ONLINE','OTHER')),
    ref_no              text,
    description         text,
    journal_entry_id    uuid NOT NULL,
    status              text NOT NULL DEFAULT 'POSTED' CHECK (status IN ('POSTED','VOID')),
    void_reason         text,
    voided_by           uuid,
    voided_at           timestamptz,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, store_id, voucher_type, voucher_no),
    CHECK (debit_account_id <> credit_account_id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, debit_account_id) REFERENCES acct.accounts(company_id, id),
    FOREIGN KEY (company_id, credit_account_id) REFERENCES acct.accounts(company_id, id),
    FOREIGN KEY (company_id, party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, journal_entry_id) REFERENCES acct.journal_entries(company_id, id)
);
CREATE INDEX ix_vouchers_date ON acct.vouchers (company_id, store_id, voucher_date);
CREATE INDEX ix_vouchers_party ON acct.vouchers (company_id, party_id, voucher_date) WHERE party_id IS NOT NULL;
CREATE TRIGGER trg_touch BEFORE UPDATE ON acct.vouchers FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- Settles documents: a payment or receipt voucher is allocated to exactly one bill, expense or payroll line.
-- FKs to sales/purchases/expenses/payroll are added after those tables exist.
CREATE TABLE acct.voucher_allocations (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    voucher_id          uuid NOT NULL,
    store_id            uuid NOT NULL,
    sale_invoice_id     uuid,
    purchase_invoice_id uuid,
    expense_id          uuid,
    payroll_line_id     uuid,
    amount              core.money NOT NULL CHECK (amount > 0),
    created_at          timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, voucher_id) REFERENCES acct.vouchers(company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    CHECK ((sale_invoice_id IS NOT NULL)::int + (purchase_invoice_id IS NOT NULL)::int
         + (expense_id IS NOT NULL)::int + (payroll_line_id IS NOT NULL)::int = 1)
);
CREATE INDEX ix_alloc_purchase ON acct.voucher_allocations (company_id, purchase_invoice_id) WHERE purchase_invoice_id IS NOT NULL;
CREATE INDEX ix_alloc_sale     ON acct.voucher_allocations (company_id, sale_invoice_id)     WHERE sale_invoice_id IS NOT NULL;
CREATE INDEX ix_alloc_expense  ON acct.voucher_allocations (company_id, expense_id)         WHERE expense_id IS NOT NULL;

-- =====================================================================
-- CATALOG: products, variants (one barcode per size + colour), prices, bands
-- =====================================================================
CREATE TABLE catalog.products (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    code                text NOT NULL,
    name                text NOT NULL,
    name_ur             text,
    department_id       uuid NOT NULL,
    category_id         uuid NOT NULL,
    brand_id            uuid,
    season_id           uuid,
    gender_id           uuid,
    product_type_id     uuid,
    unit_id             uuid,
    location_id         uuid,
    weight              numeric(10,3) CHECK (weight IS NULL OR weight >= 0),
    description         text,
    tax_rate            core.pct NOT NULL DEFAULT 0,
    tax_code            text,
    status              text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','INACTIVE')),
    attributes          jsonb NOT NULL DEFAULT '{}'::jsonb,
    is_deleted          boolean NOT NULL DEFAULT false,
    deleted_at          timestamptz,
    deleted_by          uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, department_id) REFERENCES master.departments(company_id, id),
    FOREIGN KEY (company_id, category_id)   REFERENCES master.categories(company_id, id),
    FOREIGN KEY (company_id, brand_id)      REFERENCES master.brands(company_id, id),
    FOREIGN KEY (company_id, season_id)     REFERENCES master.seasons(company_id, id),
    FOREIGN KEY (company_id, gender_id)     REFERENCES master.genders(company_id, id),
    FOREIGN KEY (company_id, product_type_id) REFERENCES master.product_types(company_id, id),
    FOREIGN KEY (company_id, unit_id)       REFERENCES master.units(company_id, id),
    FOREIGN KEY (company_id, location_id)   REFERENCES master.locations(company_id, id)
);
CREATE UNIQUE INDEX ux_products_code ON catalog.products (company_id, code) WHERE NOT is_deleted;
CREATE INDEX ix_products_category ON catalog.products (company_id, category_id) WHERE NOT is_deleted;
CREATE INDEX ix_products_name ON catalog.products (company_id, lower(name)) WHERE NOT is_deleted;
CREATE TRIGGER trg_touch BEFORE UPDATE ON catalog.products FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE catalog.product_images (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    product_id      uuid NOT NULL,
    slot            smallint NOT NULL CHECK (slot BETWEEN 1 AND 4),
    storage_key     text NOT NULL,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, product_id, slot),
    FOREIGN KEY (company_id, product_id) REFERENCES catalog.products(company_id, id)
);

CREATE TABLE catalog.variants (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    product_id      uuid NOT NULL,
    color_id        uuid,
    size_group_id   uuid,
    size_code       text NOT NULL,
    barcode         text NOT NULL CHECK (barcode <> ''),   -- one barcode per size (not per piece)
    status          text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','INACTIVE')),
    is_deleted      boolean NOT NULL DEFAULT false,
    deleted_at      timestamptz,
    deleted_by      uuid,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, product_id)    REFERENCES catalog.products(company_id, id),
    FOREIGN KEY (company_id, color_id)      REFERENCES master.colors(company_id, id),
    FOREIGN KEY (company_id, size_group_id) REFERENCES master.size_groups(company_id, id)
);
-- Barcode lookup at the counter: index-only scan.
CREATE UNIQUE INDEX ux_variants_barcode ON catalog.variants (company_id, barcode)
    INCLUDE (id, product_id) WHERE NOT is_deleted;
CREATE UNIQUE INDEX ux_variants_attr ON catalog.variants (company_id, product_id, COALESCE(color_id, '00000000-0000-0000-0000-000000000000'::uuid), size_code)
    WHERE NOT is_deleted;
CREATE INDEX ix_variants_product ON catalog.variants (company_id, product_id) WHERE NOT is_deleted;
CREATE TRIGGER trg_touch BEFORE UPDATE ON catalog.variants FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE catalog.variant_prices (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    variant_id      uuid NOT NULL,
    purchase_price  core.money NOT NULL DEFAULT 0 CHECK (purchase_price >= 0),
    sale_price      core.money NOT NULL CHECK (sale_price >= 0),          -- list (shelf) price
    change_seq      bigint NOT NULL DEFAULT nextval('core.change_seq'),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, variant_id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id)
);
CREATE INDEX ix_variant_prices_seq ON catalog.variant_prices (company_id, change_seq);
CREATE TRIGGER trg_seq BEFORE UPDATE ON catalog.variant_prices FOR EACH ROW EXECUTE FUNCTION core.bump_change_seq();

CREATE TABLE catalog.store_price_overrides (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    store_id        uuid NOT NULL,
    variant_id      uuid NOT NULL,
    sale_price      core.money NOT NULL CHECK (sale_price >= 0),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      uuid,
    PRIMARY KEY (company_id, store_id, variant_id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);

-- Append-only price history. batch_id groups bulk changes (change prices, % change, replace barcodes).
CREATE TABLE catalog.price_events (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    variant_id      uuid NOT NULL,
    store_id        uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'::uuid,
    price_type      text NOT NULL CHECK (price_type IN ('PURCHASE','SALE')),
    old_price       core.money,
    new_price       core.money NOT NULL CHECK (new_price >= 0),
    batch_id        uuid,
    reason          text,
    changed_by      uuid,
    changed_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id)
);
CREATE INDEX ix_price_events_variant ON catalog.price_events (company_id, variant_id, changed_at);

-- Owner-defined price ranges, based on the LIST price. Ranges may not overlap.
-- max_price NULL = no upper limit.
CREATE TABLE catalog.price_bands (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    name            text NOT NULL,
    min_price       core.money NOT NULL CHECK (min_price >= 0),
    max_price       core.money,
    sort_order      int NOT NULL DEFAULT 0,
    is_deleted      boolean NOT NULL DEFAULT false,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CHECK (max_price IS NULL OR max_price >= min_price),
    CONSTRAINT price_bands_no_overlap EXCLUDE USING gist (
        company_id WITH =,
        numrange(min_price, max_price, '[]') WITH &&)
        WHERE (NOT is_deleted)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON catalog.price_bands FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- =====================================================================
-- PROMOTIONS
-- A rule = one discount + target + stores + channels + time window.
-- Engine output: promo.effective_prices (one row per channel, store, variant).
-- =====================================================================
CREATE TABLE promo.rules (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    name            text NOT NULL,
    discount_type   text NOT NULL CHECK (discount_type IN ('PERCENT','AMOUNT','FIXED_PRICE')),
    discount_value  numeric(12,2) NOT NULL CHECK (discount_value >= 0),
    target_type     text NOT NULL CHECK (target_type IN ('ALL','CATEGORY','PRODUCT','VARIANT')),
    stock_min       core.qty CHECK (stock_min IS NULL OR stock_min >= 0),   -- e.g. "clear last pieces"
    stock_max       core.qty CHECK (stock_max IS NULL OR stock_max >= 0),
    starts_at       timestamptz NOT NULL,
    ends_at         timestamptz,                                              -- NULL = until ended
    priority        int NOT NULL DEFAULT 0,
    status          text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','PUBLISHED','CANCELLED')),
    is_deleted      boolean NOT NULL DEFAULT false,
    deleted_at      timestamptz,
    deleted_by      uuid,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CHECK (ends_at IS NULL OR ends_at > starts_at),
    CHECK (discount_type <> 'PERCENT' OR discount_value <= 100),
    CHECK (stock_min IS NULL OR stock_max IS NULL OR stock_max >= stock_min)
);
CREATE INDEX ix_rules_live ON promo.rules (company_id, starts_at, ends_at) WHERE status = 'PUBLISHED' AND NOT is_deleted;
CREATE TRIGGER trg_touch BEFORE UPDATE ON promo.rules FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE promo.rule_targets (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    rule_id             uuid NOT NULL,
    category_id         uuid,
    product_id          uuid,
    variant_id          uuid,
    discount_override   numeric(12,2) CHECK (discount_override IS NULL OR discount_override >= 0),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, rule_id) REFERENCES promo.rules(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, category_id) REFERENCES master.categories(company_id, id),
    FOREIGN KEY (company_id, product_id) REFERENCES catalog.products(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    CHECK ((category_id IS NOT NULL)::int + (product_id IS NOT NULL)::int + (variant_id IS NOT NULL)::int = 1)
);
CREATE UNIQUE INDEX ux_rule_targets ON promo.rule_targets (company_id, rule_id,
    COALESCE(category_id, '00000000-0000-0000-0000-000000000000'::uuid), COALESCE(product_id, '00000000-0000-0000-0000-000000000000'::uuid), COALESCE(variant_id, '00000000-0000-0000-0000-000000000000'::uuid));

CREATE TABLE promo.rule_stores (
    company_id  uuid NOT NULL REFERENCES platform.companies(id),
    rule_id     uuid NOT NULL,
    store_id    uuid NOT NULL,
    PRIMARY KEY (company_id, rule_id, store_id),
    FOREIGN KEY (company_id, rule_id) REFERENCES promo.rules(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);

CREATE TABLE promo.rule_channels (
    company_id  uuid NOT NULL REFERENCES platform.companies(id),
    rule_id     uuid NOT NULL,
    channel_id  uuid NOT NULL,
    PRIMARY KEY (company_id, rule_id, channel_id),
    FOREIGN KEY (company_id, rule_id) REFERENCES promo.rules(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, channel_id) REFERENCES integ.channels(company_id, id)
);

-- Deals: one bundle barcode sells several articles, each at its net price.
CREATE TABLE promo.bundles (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    name            text NOT NULL,
    barcode         text NOT NULL CHECK (barcode <> ''),
    bundle_price    core.money NOT NULL CHECK (bundle_price >= 0),
    starts_at       timestamptz NOT NULL,
    ends_at         timestamptz,
    status          text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','PUBLISHED','CANCELLED')),
    is_deleted      boolean NOT NULL DEFAULT false,
    deleted_at      timestamptz,
    deleted_by      uuid,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CHECK (ends_at IS NULL OR ends_at > starts_at)
);
CREATE UNIQUE INDEX ux_bundles_barcode ON promo.bundles (company_id, barcode) WHERE NOT is_deleted;
CREATE TRIGGER trg_touch BEFORE UPDATE ON promo.bundles FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE promo.bundle_items (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    bundle_id       uuid NOT NULL,
    variant_id      uuid NOT NULL,
    qty             core.qty NOT NULL CHECK (qty > 0),
    net_unit_price  core.money NOT NULL CHECK (net_unit_price >= 0),
    PRIMARY KEY (company_id, bundle_id, variant_id),
    FOREIGN KEY (company_id, bundle_id) REFERENCES promo.bundles(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id)
);

-- Conflict = two or more published rules match the same variant, store and channel.
-- The engine does not pick one silently: the item is sold at list price until an admin
-- confirms one rule (status RESOLVED). A new or changed set of rules re-opens the question.
CREATE TABLE promo.conflict_groups (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    channel_id      uuid NOT NULL,
    store_id        uuid NOT NULL,
    variant_id      uuid NOT NULL,
    candidate_key   text NOT NULL,                  -- sorted rule ids, e.g. '4,9'
    status          text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','RESOLVED','SUPERSEDED')),
    chosen_rule_id  uuid,
    resolved_by     uuid,
    resolved_at     timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, channel_id) REFERENCES integ.channels(company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    FOREIGN KEY (company_id, chosen_rule_id) REFERENCES promo.rules(company_id, id),
    CHECK (status <> 'RESOLVED' OR chosen_rule_id IS NOT NULL)
);
CREATE UNIQUE INDEX ux_conflict_open ON promo.conflict_groups (company_id, channel_id, store_id, variant_id)
    WHERE status = 'OPEN';

CREATE TABLE promo.conflict_candidates (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    group_id            uuid NOT NULL,
    rule_id             uuid NOT NULL,
    sale_price          core.money NOT NULL CHECK (sale_price >= 0),
    discount_percent    core.pct,
    PRIMARY KEY (company_id, group_id, rule_id),
    FOREIGN KEY (company_id, group_id) REFERENCES promo.conflict_groups(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, rule_id) REFERENCES promo.rules(company_id, id)
);

-- Price a POS or Shopify line reads by primary key. Store 0 = channel-wide (Shopify).
CREATE TABLE promo.effective_prices (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    channel_id          uuid NOT NULL,
    store_id            uuid NOT NULL,
    variant_id          uuid NOT NULL,
    list_price          core.money NOT NULL CHECK (list_price >= 0),
    sale_price          core.money NOT NULL,
    discount_amount     core.money NOT NULL DEFAULT 0,
    discount_percent    core.pct,
    rule_id             uuid,
    is_conflict         boolean NOT NULL DEFAULT false,
    conflict_group_id   uuid,
    valid_from          timestamptz NOT NULL,
    valid_until         timestamptz,
    computed_at         timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, channel_id, store_id, variant_id),
    CHECK (sale_price >= 0 AND sale_price <= list_price),
    FOREIGN KEY (company_id, channel_id) REFERENCES integ.channels(company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    FOREIGN KEY (company_id, rule_id) REFERENCES promo.rules(company_id, id),
    FOREIGN KEY (company_id, conflict_group_id) REFERENCES promo.conflict_groups(company_id, id)
);
CREATE INDEX ix_eff_valid_until ON promo.effective_prices (company_id, valid_until) WHERE valid_until IS NOT NULL;

-- =====================================================================
-- INVENTORY
-- Stock on hand is updated inside the same transaction as the document (inv.post_stock_move).
-- qty_on_hand can never go negative (CHECK). Store 0 is not a stock location.
-- =====================================================================
CREATE TABLE inv.stock_balances (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    store_id        uuid NOT NULL,
    variant_id      uuid NOT NULL,
    qty_on_hand     core.qty NOT NULL DEFAULT 0 CHECK (qty_on_hand >= 0),
    avg_cost        core.unit_cost NOT NULL DEFAULT 0 CHECK (avg_cost >= 0),
    last_receipt_at timestamptz,                                  -- for aging reports
    change_seq      bigint NOT NULL DEFAULT nextval('core.change_seq'),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, store_id, variant_id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);
CREATE INDEX ix_balances_variant ON inv.stock_balances (company_id, variant_id);
CREATE INDEX ix_balances_seq     ON inv.stock_balances (company_id, change_seq);
CREATE TRIGGER trg_seq BEFORE UPDATE ON inv.stock_balances FOR EACH ROW EXECUTE FUNCTION core.bump_change_seq();

CREATE TABLE inv.stock_ledger (
    company_id      uuid NOT NULL,
    id              uuid NOT NULL DEFAULT uuidv7(),
    store_id        uuid NOT NULL,
    variant_id      uuid NOT NULL,
    txn_date        date NOT NULL,                  -- store-local business date
    txn_at          timestamptz NOT NULL DEFAULT now(),
    doc_type        text NOT NULL CHECK (doc_type IN (
                        'OPENING','SALE','SALE_RETURN','SALE_VOID','PURCHASE','PURCHASE_RETURN',
                        'ADJUSTMENT','AUDIT','CLAIM','TRANSFER_OUT','TRANSFER_IN','TRANSFER_FOUND')),
    doc_id          uuid NOT NULL,
    qty             core.qty NOT NULL CHECK (qty <> 0),
    unit_cost       core.unit_cost NOT NULL DEFAULT 0,
    qty_after       core.qty NOT NULL CHECK (qty_after >= 0),
    PRIMARY KEY (company_id, id, txn_date),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
) PARTITION BY RANGE (txn_date);
CREATE INDEX ix_ledger_item ON inv.stock_ledger (company_id, store_id, variant_id, txn_date);
CREATE INDEX ix_ledger_doc  ON inv.stock_ledger (company_id, doc_type, doc_id);

CREATE TABLE inv.documents (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    store_id        uuid NOT NULL,
    doc_type        text NOT NULL CHECK (doc_type IN ('ADJUSTMENT','AUDIT','CLAIM')),
    doc_no          bigint NOT NULL,
    doc_date        date NOT NULL,
    status          text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','POSTED','APPROVED','REJECTED','CANCELLED')),
    party_id        uuid,                         -- vendor, for claims
    reason          text,
    document_uuid   uuid NOT NULL DEFAULT gen_random_uuid(),
    posted_at       timestamptz,
    posted_by       uuid,
    is_deleted      boolean NOT NULL DEFAULT false,
    deleted_at      timestamptz,
    deleted_by      uuid,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, document_uuid),
    UNIQUE (company_id, store_id, doc_type, doc_no),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, party_id) REFERENCES party.parties(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON inv.documents FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE inv.document_lines (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    document_id     uuid NOT NULL,
    line_no         int NOT NULL,
    variant_id      uuid NOT NULL,
    expected_qty    core.qty,                       -- audits: book quantity at count time
    counted_qty     core.qty,                       -- audits: physical count
    qty             core.qty NOT NULL CHECK (qty <> 0),   -- signed movement; audits: counted - expected
    unit_cost       core.unit_cost NOT NULL DEFAULT 0,
    reason          text,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, document_id, line_no),
    FOREIGN KEY (company_id, document_id) REFERENCES inv.documents(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    CHECK (counted_qty IS NULL OR expected_qty IS NULL OR counted_qty - expected_qty = qty)
);

CREATE TABLE inv.transfers (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    transfer_no     bigint NOT NULL,
    from_store_id   uuid NOT NULL,
    to_store_id     uuid NOT NULL,
    status          text NOT NULL DEFAULT 'DRAFT' CHECK (status IN
                        ('DRAFT','SHIPPED','RECEIVING','COMPLETED','COMPLETED_WITH_MISSING','CANCELLED')),
    note            text,
    document_uuid   uuid NOT NULL DEFAULT gen_random_uuid(),
    created_by      uuid,
    created_at      timestamptz NOT NULL DEFAULT now(),
    shipped_by      uuid,
    shipped_at      timestamptz,
    closed_by       uuid,
    closed_at       timestamptz,
    is_deleted      boolean NOT NULL DEFAULT false,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, document_uuid),
    UNIQUE (company_id, from_store_id, transfer_no),
    FOREIGN KEY (company_id, from_store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, to_store_id)   REFERENCES core.stores(company_id, id),
    CHECK (from_store_id <> to_store_id),
    CHECK (from_store_id <> '00000000-0000-0000-0000-000000000000'::uuid AND to_store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);
CREATE INDEX ix_transfers_incoming ON inv.transfers (company_id, to_store_id, status);
CREATE INDEX ix_transfers_outgoing ON inv.transfers (company_id, from_store_id, status);
CREATE TRIGGER trg_touch BEFORE UPDATE ON inv.transfers FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE inv.transfer_lines (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    transfer_id     uuid NOT NULL,
    variant_id      uuid NOT NULL,
    qty_scanned     core.qty NOT NULL DEFAULT 0 CHECK (qty_scanned >= 0),   -- pieces packed by sender
    qty_shipped     core.qty NOT NULL DEFAULT 0 CHECK (qty_shipped >= 0),
    qty_received    core.qty NOT NULL DEFAULT 0 CHECK (qty_received >= 0),  -- counted by receiver
    qty_missing     core.qty NOT NULL DEFAULT 0 CHECK (qty_missing >= 0),   -- = sum of open/decided loss cases
    unit_cost       core.unit_cost NOT NULL DEFAULT 0,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, transfer_id, variant_id),
    CHECK (qty_received + qty_missing <= qty_shipped),
    FOREIGN KEY (company_id, transfer_id) REFERENCES inv.transfers(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id)
);

CREATE TABLE inv.transfer_events (                  -- append-only audit of every step
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    transfer_id     uuid NOT NULL,
    variant_id      uuid,
    event_type      text NOT NULL CHECK (event_type IN ('SCAN','SHIP','RECEIVE','COMPLETE','MISSING_RAISED',
                        'LOSS_ASSIGNED','MISSING_FOUND','LOSS_WRITTEN_OFF','CANCEL')),
    qty             core.qty,
    reason          text,
    user_id         uuid,
    occurred_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, transfer_id) REFERENCES inv.transfers(company_id, id)
);
CREATE INDEX ix_transfer_events ON inv.transfer_events (company_id, transfer_id, occurred_at);

-- Missing pieces, one case per decision. Admin sees all cases in inv.v_transfer_loss_board.
-- bearer decides who the loss is charged to:
--   COMPANY        -> head office stock-loss expense
--   SENDER_STORE   -> sending store's stock-loss expense
--   RECEIVER_STORE -> receiving store's stock-loss expense
-- FOUND moves the pieces into the receiver's stock instead.
CREATE TABLE inv.transfer_loss_cases (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    transfer_id         uuid NOT NULL,
    transfer_line_id    uuid NOT NULL,
    qty                 core.qty NOT NULL CHECK (qty > 0),
    reason              text NOT NULL,
    bearer              text CHECK (bearer IN ('COMPANY','SENDER_STORE','RECEIVER_STORE')),
    status              text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','ASSIGNED','FOUND','WRITTEN_OFF')),
    raised_by           uuid,
    raised_at           timestamptz NOT NULL DEFAULT now(),
    decided_by          uuid,
    decided_at          timestamptz,
    journal_entry_id    uuid,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, transfer_id) REFERENCES inv.transfers(company_id, id),
    FOREIGN KEY (company_id, transfer_line_id) REFERENCES inv.transfer_lines(company_id, id),
    FOREIGN KEY (company_id, journal_entry_id) REFERENCES acct.journal_entries(company_id, id),
    CHECK (status NOT IN ('ASSIGNED','WRITTEN_OFF') OR bearer IS NOT NULL)
);
CREATE INDEX ix_loss_open ON inv.transfer_loss_cases (company_id, status) WHERE status = 'OPEN';

-- =====================================================================
-- ORDERS (sale order, sale return order, purchase order, vendor/company PO)
-- =====================================================================
CREATE TABLE orders.orders (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    order_type          text NOT NULL CHECK (order_type IN ('SALE','SALE_RETURN','PURCHASE','PURCHASE_RETURN','VENDOR_PO','COMPANY_PO')),
    order_no            bigint NOT NULL,
    order_date          date NOT NULL,
    party_id            uuid,
    bill_discount       core.money NOT NULL DEFAULT 0 CHECK (bill_discount >= 0),
    status              text NOT NULL DEFAULT 'NOT_POSTED' CHECK (status IN ('NOT_POSTED','PARTIAL','POSTED','CANCELLED')),
    document_uuid       uuid NOT NULL DEFAULT gen_random_uuid(),
    is_deleted          boolean NOT NULL DEFAULT false,
    deleted_at          timestamptz,
    deleted_by          uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, document_uuid),
    UNIQUE (company_id, store_id, order_type, order_no),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, party_id) REFERENCES party.parties(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);
CREATE INDEX ix_orders_status ON orders.orders (company_id, store_id, order_type, status, order_date);
CREATE TRIGGER trg_touch BEFORE UPDATE ON orders.orders FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE orders.order_lines (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    order_id            uuid NOT NULL,
    line_no             int NOT NULL,
    variant_id          uuid NOT NULL,
    qty_ordered         core.qty NOT NULL CHECK (qty_ordered > 0),
    qty_posted          core.qty NOT NULL DEFAULT 0 CHECK (qty_posted >= 0),
    unit_price          core.money NOT NULL DEFAULT 0 CHECK (unit_price >= 0),
    discount_amount     core.money NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, order_id, line_no),
    CHECK (qty_posted <= qty_ordered),
    FOREIGN KEY (company_id, order_id) REFERENCES orders.orders(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id)
);

-- =====================================================================
-- SALES
-- Sign convention: RETURN documents carry negative qty and negative amounts.
-- store_id = the store the stock leaves from (POS store, or fulfilling store for Shopify).
-- channel_id decides where the sale is reported (a Shopify order is reported under SHOPIFY, not the store POS).
-- Offline POS: device_sale_uuid makes sync idempotent; occurred_at = device time; received_at = server time.
-- =====================================================================
CREATE TABLE sales.invoices (
    company_id              uuid NOT NULL REFERENCES platform.companies(id),
    id                      uuid NOT NULL DEFAULT uuidv7(),
    store_id                uuid NOT NULL,
    channel_id              uuid NOT NULL,
    doc_type                text NOT NULL CHECK (doc_type IN ('SALE','RETURN')),
    invoice_no              bigint NOT NULL,                  -- core.next_number(series by doc_type)
    sale_date               date NOT NULL,                    -- store-local date
    daily_bill_no           integer CHECK (daily_bill_no IS NULL OR daily_bill_no > 0),
    device_id               uuid,
    device_sale_uuid        uuid,
    occurred_at             timestamptz NOT NULL,
    received_at             timestamptz NOT NULL DEFAULT now(),
    original_invoice_id     uuid,
    related_invoice_id      uuid,                           -- exchange: return and new sale point at each other
    customer_party_id       uuid,
    salesman_party_id       uuid,
    walkin_name             text,
    walkin_mobile           text,
    online_order_ref        text,                             -- Shopify order name / number
    gross_amount            core.money NOT NULL DEFAULT 0,
    line_discount_amount    core.money NOT NULL DEFAULT 0,
    bill_discount_amount    core.money NOT NULL DEFAULT 0,
    taxable_amount          core.money NOT NULL DEFAULT 0,
    tax_amount              core.money NOT NULL DEFAULT 0,
    net_amount              core.money NOT NULL DEFAULT 0,
    paid_amount             core.money NOT NULL DEFAULT 0,
    change_amount           core.money NOT NULL DEFAULT 0,
    credit_amount           core.money NOT NULL DEFAULT 0,    -- unpaid part booked to the customer account
    cost_amount             core.money NOT NULL DEFAULT 0,
    has_price_drift         boolean NOT NULL DEFAULT false,
    has_shortfall           boolean NOT NULL DEFAULT false,
    status                  text NOT NULL DEFAULT 'POSTED' CHECK (status IN ('POSTED','VOID')),
    void_reason             text,
    voided_by               uuid,
    voided_at               timestamptz,
    buyer_ntn               text,                             -- tax-ready fields
    buyer_cnic              text,
    journal_entry_id        uuid,
    created_at              timestamptz NOT NULL DEFAULT now(),
    created_by              uuid,
    updated_at              timestamptz,
    updated_by              uuid,
    row_ver                 bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, store_id, doc_type, invoice_no),
    UNIQUE (company_id, device_sale_uuid),
    FOREIGN KEY (company_id, store_id)       REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, channel_id)     REFERENCES integ.channels(company_id, id),
    FOREIGN KEY (company_id, device_id)      REFERENCES core.devices(company_id, id),
    FOREIGN KEY (company_id, customer_party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, salesman_party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, original_invoice_id) REFERENCES sales.invoices(company_id, id),
    FOREIGN KEY (company_id, related_invoice_id)  REFERENCES sales.invoices(company_id, id),
    FOREIGN KEY (company_id, journal_entry_id)    REFERENCES acct.journal_entries(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CHECK (doc_type <> 'RETURN' OR original_invoice_id IS NOT NULL),
    CHECK ((doc_type = 'SALE' AND net_amount >= 0) OR (doc_type = 'RETURN' AND net_amount <= 0)),
    CHECK (credit_amount >= 0 AND paid_amount >= 0)
);
CREATE UNIQUE INDEX ux_invoices_daily_bill ON sales.invoices (company_id, store_id, sale_date, daily_bill_no)
    WHERE doc_type = 'SALE' AND daily_bill_no IS NOT NULL;
CREATE UNIQUE INDEX ux_invoices_online_ref ON sales.invoices (company_id, channel_id, online_order_ref)
    WHERE online_order_ref IS NOT NULL;
CREATE INDEX ix_invoices_store_date   ON sales.invoices (company_id, store_id, sale_date) INCLUDE (channel_id, doc_type, net_amount);
CREATE INDEX ix_invoices_channel_date ON sales.invoices (company_id, channel_id, sale_date);
CREATE INDEX ix_invoices_customer     ON sales.invoices (company_id, customer_party_id, sale_date) WHERE customer_party_id IS NOT NULL;
CREATE INDEX ix_invoices_salesman     ON sales.invoices (company_id, salesman_party_id, sale_date) WHERE salesman_party_id IS NOT NULL;
CREATE TRIGGER trg_touch BEFORE UPDATE ON sales.invoices FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- Partitioned by sale_date (monthly). The largest table: ~22M lines a year at 50 stores.
CREATE TABLE sales.invoice_lines (
    company_id          uuid NOT NULL,
    id                  uuid NOT NULL DEFAULT uuidv7(),
    invoice_id          uuid NOT NULL,
    line_no             integer NOT NULL,
    store_id            uuid NOT NULL,
    channel_id          uuid NOT NULL,
    sale_date           date NOT NULL,
    doc_type            text NOT NULL CHECK (doc_type IN ('SALE','RETURN')),
    variant_id          uuid NOT NULL,
    product_id          uuid NOT NULL,
    category_id         uuid NOT NULL,
    department_id       uuid NOT NULL,
    brand_id            uuid,
    season_id           uuid,
    gender_id           uuid,
    color_id            uuid,
    size_group_id       uuid,
    product_type_id     uuid,
    salesman_party_id   uuid,
    price_band_id       uuid,                              -- NULL = unbanded
    qty                 core.qty NOT NULL CHECK (qty <> 0),
    list_price          core.money NOT NULL CHECK (list_price >= 0),   -- band is taken from this
    unit_price          core.money NOT NULL CHECK (unit_price >= 0),   -- charged per unit
    discount_source     text NOT NULL DEFAULT 'NONE' CHECK (discount_source IN ('NONE','RULE','MANUAL','BUNDLE')),
    rule_id             uuid,
    conflict_group_id   uuid,
    bundle_id           uuid,
    manual_approved_by  uuid,
    discount_amount     core.money NOT NULL DEFAULT 0,
    tax_rate            core.pct NOT NULL DEFAULT 0,
    taxable_amount      core.money NOT NULL DEFAULT 0,
    tax_amount          core.money NOT NULL DEFAULT 0,
    net_amount          core.money NOT NULL DEFAULT 0,
    unit_cost           core.unit_cost NOT NULL DEFAULT 0,
    cost_amount         core.money NOT NULL DEFAULT 0,
    qty_short           core.qty NOT NULL DEFAULT 0 CHECK (qty_short >= 0),
    original_line_id        uuid,                 -- RETURN lines: the SALE line being returned
    original_line_sale_date date,
    CHECK (doc_type = 'SALE' OR original_line_id IS NOT NULL),
    PRIMARY KEY (company_id, id, sale_date),
    UNIQUE (company_id, invoice_id, line_no, sale_date),
    FOREIGN KEY (company_id, invoice_id) REFERENCES sales.invoices(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    FOREIGN KEY (company_id, rule_id)    REFERENCES promo.rules(company_id, id),
    FOREIGN KEY (company_id, conflict_group_id) REFERENCES promo.conflict_groups(company_id, id),
    CHECK ((doc_type = 'SALE' AND qty > 0) OR (doc_type = 'RETURN' AND qty < 0)),
    CHECK (discount_source <> 'RULE' OR rule_id IS NOT NULL)
) PARTITION BY RANGE (sale_date);
CREATE INDEX ix_lines_store_date   ON sales.invoice_lines (company_id, store_id, sale_date);
CREATE INDEX ix_lines_channel_date ON sales.invoice_lines (company_id, channel_id, sale_date);
CREATE INDEX ix_lines_variant_date ON sales.invoice_lines (company_id, variant_id, sale_date);
CREATE INDEX ix_lines_band_date    ON sales.invoice_lines (company_id, price_band_id, sale_date);
CREATE INDEX ix_lines_salesman     ON sales.invoice_lines (company_id, salesman_party_id, sale_date);
CREATE INDEX ix_lines_invoice      ON sales.invoice_lines (company_id, invoice_id);

CREATE TABLE sales.payments (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    invoice_id          uuid NOT NULL,
    store_id            uuid NOT NULL,
    sale_date           date NOT NULL,
    method              text NOT NULL CHECK (method IN ('CASH','CARD','BANK_TRANSFER','CHEQUE','ONLINE')),
    payment_account_id  uuid NOT NULL,
    amount              core.money NOT NULL CHECK (amount <> 0),       -- negative = refund paid out
    reference           text,
    received_at         timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, invoice_id) REFERENCES sales.invoices(company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, payment_account_id) REFERENCES acct.accounts(company_id, id)
);
CREATE INDEX ix_payments_invoice ON sales.payments (company_id, invoice_id);
CREATE INDEX ix_payments_store_date ON sales.payments (company_id, store_id, sale_date);

-- Offline sale accepted at a price that is no longer valid at the server. Accepted, never rejected.
CREATE TABLE sales.price_drift_records (
    company_id              uuid NOT NULL REFERENCES platform.companies(id),
    id                      uuid NOT NULL DEFAULT uuidv7(),
    store_id                uuid NOT NULL,
    channel_id              uuid NOT NULL,
    invoice_id              uuid NOT NULL,
    variant_id              uuid NOT NULL,
    rule_id                 uuid,
    occurred_at             timestamptz NOT NULL,
    received_at             timestamptz NOT NULL DEFAULT now(),
    qty                     core.qty NOT NULL,
    expected_unit_price     core.money NOT NULL,
    charged_unit_price      core.money NOT NULL,
    difference_amount       numeric(16,3) GENERATED ALWAYS AS ((expected_unit_price - charged_unit_price) * qty) STORED,
    reason                  text NOT NULL CHECK (reason IN ('RULE_ENDED','RULE_NOT_STARTED','PRICE_CHANGED')),
    status                  text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','REVIEWED')),
    reviewed_by             uuid,
    reviewed_at             timestamptz,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, invoice_id) REFERENCES sales.invoices(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    FOREIGN KEY (company_id, rule_id) REFERENCES promo.rules(company_id, id)
);
CREATE INDEX ix_drift_open ON sales.price_drift_records (company_id, status) WHERE status = 'OPEN';

-- =====================================================================
-- PURCHASES AND VENDOR PAYMENTS
-- Vendor balance = party_balances (ledger). Remaining per bill = v_invoice_balance.
-- Next payment = vendor_payment_schedule (due dates and planned amounts).
-- =====================================================================
CREATE TABLE purch.invoices (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    doc_type            text NOT NULL CHECK (doc_type IN ('PURCHASE','RETURN')),
    invoice_no          bigint NOT NULL,
    vendor_bill_no      text,                              -- the supplier's own bill number
    vendor_party_id     uuid NOT NULL,
    bill_date           date NOT NULL,
    received_on         date,
    due_date            date,                              -- credit terms
    gross_amount        core.money NOT NULL DEFAULT 0,
    discount_amount     core.money NOT NULL DEFAULT 0,
    tax_amount          core.money NOT NULL DEFAULT 0,
    net_amount          core.money NOT NULL DEFAULT 0,     -- payable (negative for RETURN)
    status              text NOT NULL DEFAULT 'POSTED' CHECK (status IN ('POSTED','VOID')),
    void_reason         text,
    voided_by           uuid,
    voided_at           timestamptz,
    journal_entry_id    uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, store_id, doc_type, invoice_no),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, vendor_party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, journal_entry_id) REFERENCES acct.journal_entries(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CHECK ((doc_type = 'PURCHASE' AND net_amount >= 0) OR (doc_type = 'RETURN' AND net_amount <= 0))
);
-- The same vendor bill cannot be entered twice.
CREATE UNIQUE INDEX ux_purch_vendor_bill ON purch.invoices (company_id, vendor_party_id, vendor_bill_no)
    WHERE doc_type = 'PURCHASE' AND vendor_bill_no IS NOT NULL AND status = 'POSTED';
CREATE INDEX ix_purch_vendor_date ON purch.invoices (company_id, vendor_party_id, bill_date);
CREATE INDEX ix_purch_store_date  ON purch.invoices (company_id, store_id, bill_date);
CREATE TRIGGER trg_touch BEFORE UPDATE ON purch.invoices FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE purch.invoice_lines (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    invoice_id          uuid NOT NULL,
    line_no             int NOT NULL,
    variant_id          uuid NOT NULL,
    qty                 core.qty NOT NULL CHECK (qty <> 0),
    unit_cost           core.money NOT NULL CHECK (unit_cost >= 0),        -- purchase price per piece
    sale_price          core.money NOT NULL CHECK (sale_price >= 0),
    sale_discount_pct   core.pct,
    line_amount         core.money NOT NULL,
    updates_sale_price  boolean NOT NULL DEFAULT false,
    original_line_id    uuid,                                            -- for returns
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, invoice_id, line_no),
    FOREIGN KEY (company_id, invoice_id) REFERENCES purch.invoices(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    FOREIGN KEY (company_id, original_line_id) REFERENCES purch.invoice_lines(company_id, id)
);

CREATE TABLE purch.vendor_payment_schedule (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    vendor_party_id     uuid NOT NULL,
    purchase_invoice_id uuid,                          -- NULL = planned payment not tied to one bill
    due_date            date NOT NULL,
    planned_amount      core.money NOT NULL CHECK (planned_amount > 0),
    paid_amount         core.money NOT NULL DEFAULT 0 CHECK (paid_amount >= 0),
    status              text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','PARTIAL','PAID','CANCELLED')),
    note                text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CHECK (paid_amount <= planned_amount),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, vendor_party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, purchase_invoice_id) REFERENCES purch.invoices(company_id, id)
);
CREATE INDEX ix_vendor_sched_due ON purch.vendor_payment_schedule (company_id, vendor_party_id, due_date)
    WHERE status IN ('OPEN','PARTIAL');
CREATE TRIGGER trg_touch BEFORE UPDATE ON purch.vendor_payment_schedule FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- =====================================================================
-- CASH: customer promises, end-of-day cash counts
-- =====================================================================
CREATE TABLE cash.promises (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    customer_party_id   uuid NOT NULL,
    promise_date        date NOT NULL DEFAULT CURRENT_DATE,
    due_date            date NOT NULL,
    promised_amount     core.money NOT NULL CHECK (promised_amount > 0),
    paid_amount         core.money NOT NULL DEFAULT 0 CHECK (paid_amount >= 0),
    status              text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','PARTIAL','PAID','BROKEN','CANCELLED')),
    note                text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CHECK (paid_amount <= promised_amount),
    CHECK (due_date >= promise_date),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, customer_party_id) REFERENCES party.parties(company_id, id)
);
CREATE INDEX ix_promises_due ON cash.promises (company_id, due_date) WHERE status IN ('OPEN','PARTIAL');
CREATE TRIGGER trg_touch BEFORE UPDATE ON cash.promises FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE cash.cash_counts (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    count_date          date NOT NULL,
    opening_cash        core.money NOT NULL DEFAULT 0,
    expected_cash       core.money NOT NULL DEFAULT 0,       -- snapshot of what the system expects
    counted_cash        core.money NOT NULL DEFAULT 0,
    difference          core.money GENERATED ALWAYS AS (counted_cash - expected_cash) STORED,
    counted_by          uuid,
    note                text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, store_id, count_date),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id)
);

CREATE TABLE cash.count_denominations (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    count_id        uuid NOT NULL,
    denomination    int NOT NULL CHECK (denomination IN (5000,1000,500,100,50,20,10,5,2,1)),
    note_count      int NOT NULL CHECK (note_count >= 0),
    PRIMARY KEY (company_id, count_id, denomination),
    FOREIGN KEY (company_id, count_id) REFERENCES cash.cash_counts(company_id, id) ON DELETE CASCADE
);

-- =====================================================================
-- HR: commission plans, members, rules, per-line entries (EARN / REVERSAL)
-- Commission is accrued PER INVOICE LINE by trigger on sales.invoice_lines (see V5 FUNCTIONS).
-- Entries go PENDING -> APPROVED (hr.approve_commissions) -> PAID (salary payment).
-- A return or a voided invoice posts REVERSAL rows that point at the original EARN row.
-- =====================================================================
CREATE TABLE hr.commission_plans (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    name            text NOT NULL,
    applies_to      text NOT NULL CHECK (applies_to IN ('SALESMAN','MANAGER')),
    basis           text NOT NULL CHECK (basis IN ('NET_SALE','GROSS_MARGIN')),
    calc_type       text NOT NULL DEFAULT 'PERCENT' CHECK (calc_type IN ('PERCENT','FIXED_PER_PIECE')),
    default_rate    core.pct,                                   -- percent of base, when calc_type = PERCENT
    default_fixed   core.money CHECK (default_fixed IS NULL OR default_fixed >= 0),   -- per piece
    min_store_sales core.money NOT NULL DEFAULT 0 CHECK (min_store_sales >= 0),      -- MANAGER: store sales needed before approval
    store_id        uuid,                                     -- NULL = all stores
    valid_from      date NOT NULL,
    valid_to        date,
    is_active       boolean NOT NULL DEFAULT true,
    is_deleted      boolean NOT NULL DEFAULT false,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CHECK ((calc_type = 'PERCENT' AND default_rate IS NOT NULL)
        OR (calc_type = 'FIXED_PER_PIECE' AND default_fixed IS NOT NULL)),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON hr.commission_plans FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- Individual plan = one member; group plan = several members; no members = every staff member of applies_to.
CREATE TABLE hr.commission_plan_members (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    plan_id             uuid NOT NULL,
    employee_party_id   uuid NOT NULL,
    PRIMARY KEY (company_id, plan_id, employee_party_id),
    FOREIGN KEY (company_id, plan_id) REFERENCES hr.commission_plans(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, employee_party_id) REFERENCES party.parties(company_id, id)
);

-- Per product, category or price band. Most specific scope wins: PRODUCT > CATEGORY > PRICE_BAND.
-- Changing a rate = end the old row (valid_to) and insert a new row; history is never rewritten.
CREATE TABLE hr.commission_rules (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    plan_id         uuid NOT NULL,
    rule_scope      text NOT NULL CHECK (rule_scope IN ('PRODUCT','CATEGORY','PRICE_BAND')),
    product_id      uuid,
    category_id     uuid,
    price_band_id   uuid,
    calc_type       text NOT NULL CHECK (calc_type IN ('PERCENT','FIXED_PER_PIECE')),
    rate            core.pct,
    fixed_amount    core.money CHECK (fixed_amount IS NULL OR fixed_amount >= 0),
    min_line_amount core.money NOT NULL DEFAULT 0 CHECK (min_line_amount >= 0),
    priority        int NOT NULL DEFAULT 0,
    valid_from      date NOT NULL,
    valid_to        date,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CHECK (valid_to IS NULL OR valid_to >= valid_from),
    CHECK ((rule_scope = 'PRODUCT'    AND product_id IS NOT NULL AND category_id IS NULL AND price_band_id IS NULL)
        OR (rule_scope = 'CATEGORY'   AND category_id IS NOT NULL AND product_id IS NULL AND price_band_id IS NULL)
        OR (rule_scope = 'PRICE_BAND' AND price_band_id IS NOT NULL AND product_id IS NULL AND category_id IS NULL)),
    CHECK ((calc_type = 'PERCENT' AND rate IS NOT NULL AND fixed_amount IS NULL)
        OR (calc_type = 'FIXED_PER_PIECE' AND fixed_amount IS NOT NULL AND rate IS NULL)),
    CONSTRAINT commission_rules_no_overlap EXCLUDE USING gist (
        company_id WITH =, plan_id WITH =, rule_scope WITH =,
        (COALESCE(product_id, '00000000-0000-0000-0000-000000000000'::uuid)) WITH =, (COALESCE(category_id, '00000000-0000-0000-0000-000000000000'::uuid)) WITH =, (COALESCE(price_band_id, '00000000-0000-0000-0000-000000000000'::uuid)) WITH =,
        daterange(valid_from, valid_to, '[]') WITH &&),
    FOREIGN KEY (company_id, plan_id) REFERENCES hr.commission_plans(company_id, id),
    FOREIGN KEY (company_id, product_id)    REFERENCES catalog.products(company_id, id),
    FOREIGN KEY (company_id, category_id)   REFERENCES master.categories(company_id, id),
    FOREIGN KEY (company_id, price_band_id) REFERENCES catalog.price_bands(company_id, id)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON hr.commission_rules FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- One row per invoice line per employee per plan (EARN), and one reversal per EARN row (REVERSAL).
-- Sign: SALE lines are positive, RETURN lines and reversals are negative.
CREATE TABLE hr.commission_entries (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    sale_date           date NOT NULL,                  -- business date of the posting line
    invoice_id          uuid NOT NULL,
    invoice_line_id     uuid NOT NULL,
    line_sale_date      date NOT NULL,                  -- partition key of sales.invoice_lines
    employee_party_id   uuid NOT NULL,
    plan_id             uuid NOT NULL,
    rule_id             uuid,                         -- NULL = plan default rate
    entry_type          text NOT NULL CHECK (entry_type IN ('EARN','REVERSAL')),
    reverses_entry_id   uuid,
    calc_type           text NOT NULL CHECK (calc_type IN ('PERCENT','FIXED_PER_PIECE')),
    rate                core.pct,                       -- snapshot of the rate used
    fixed_amount        core.money,                     -- snapshot of the per-piece amount used
    qty                 core.qty NOT NULL,
    base_amount         core.money NOT NULL,
    commission_amount   core.money NOT NULL,
    status              text NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','APPROVED','ON_HOLD','PAID','CANCELLED')),
    approved_by         uuid,
    approved_at         timestamptz,
    payroll_line_id     uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CHECK ((entry_type = 'EARN' AND reverses_entry_id IS NULL)
        OR (entry_type = 'REVERSAL' AND reverses_entry_id IS NOT NULL)),
    CHECK ((calc_type = 'PERCENT' AND rate IS NOT NULL) OR (calc_type = 'FIXED_PER_PIECE' AND fixed_amount IS NOT NULL)),
    FOREIGN KEY (company_id, store_id)          REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, invoice_id)        REFERENCES sales.invoices(company_id, id),
    FOREIGN KEY (company_id, invoice_line_id, line_sale_date) REFERENCES sales.invoice_lines(company_id, id, sale_date),
    FOREIGN KEY (company_id, employee_party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, plan_id)           REFERENCES hr.commission_plans(company_id, id),
    FOREIGN KEY (company_id, rule_id)           REFERENCES hr.commission_rules(company_id, id),
    FOREIGN KEY (company_id, reverses_entry_id) REFERENCES hr.commission_entries(company_id, id)
);
-- Idempotency: a line can be accrued once per employee per plan; a sale can be reversed once.
CREATE UNIQUE INDEX ux_comm_earn_once ON hr.commission_entries (company_id, invoice_line_id, line_sale_date, employee_party_id, plan_id)
    WHERE entry_type = 'EARN';
CREATE UNIQUE INDEX ux_comm_reverse_once ON hr.commission_entries (company_id, reverses_entry_id)
    WHERE entry_type = 'REVERSAL';
CREATE INDEX ix_comm_employee ON hr.commission_entries (company_id, employee_party_id, sale_date);
CREATE INDEX ix_comm_store    ON hr.commission_entries (company_id, store_id, sale_date);
CREATE INDEX ix_comm_pending  ON hr.commission_entries (company_id, store_id, sale_date) WHERE status = 'PENDING';
CREATE TRIGGER trg_touch BEFORE UPDATE ON hr.commission_entries FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- Salary history: every change to a staff member's base salary or pay frequency is kept (effective-dated).
-- Filled by trigger on party.staff_profiles.
CREATE TABLE hr.salary_terms (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    employee_party_id   uuid NOT NULL,
    effective_from      date NOT NULL,
    base_salary         core.money NOT NULL CHECK (base_salary >= 0),
    pay_frequency       text NOT NULL CHECK (pay_frequency IN ('MONTHLY','BIWEEKLY','WEEKLY')),
    changed_by          uuid,
    changed_at          timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, employee_party_id, effective_from),
    FOREIGN KEY (company_id, employee_party_id) REFERENCES party.parties(company_id, id)
);

CREATE TABLE hr.payroll_runs (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    period_month        date NOT NULL CHECK (EXTRACT(DAY FROM period_month) = 1),
    status              text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','APPROVED','CANCELLED')),
    approved_by         uuid,
    approved_at         timestamptz,
    journal_entry_id    uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, store_id, period_month),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, journal_entry_id) REFERENCES acct.journal_entries(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON hr.payroll_runs FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- One live line per employee per month (cancelled lines are kept for history when a draft is rebuilt).
CREATE TABLE hr.payroll_lines (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    payroll_run_id      uuid NOT NULL,
    store_id            uuid NOT NULL,                  -- store the employee worked in that month
    employee_party_id   uuid NOT NULL,
    period_month        date NOT NULL CHECK (EXTRACT(DAY FROM period_month) = 1),
    base_salary         core.money NOT NULL DEFAULT 0 CHECK (base_salary >= 0),
    total_sales         core.money NOT NULL DEFAULT 0,    -- "T Sale" on the salary sheet (net of returns)
    commission_amount   core.money NOT NULL DEFAULT 0,    -- sum of APPROVED commission entries in the month
    incentive_amount    core.money NOT NULL DEFAULT 0 CHECK (incentive_amount >= 0),
    deduction_amount    core.money NOT NULL DEFAULT 0 CHECK (deduction_amount >= 0),   -- advances, absences
    net_pay             core.money GENERATED ALWAYS AS (base_salary + commission_amount + incentive_amount - deduction_amount) STORED,
    paid_amount         core.money NOT NULL DEFAULT 0 CHECK (paid_amount >= 0),
    outstanding_amount  core.money GENERATED ALWAYS AS (base_salary + commission_amount + incentive_amount - deduction_amount - paid_amount) STORED,
    status              text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','APPROVED','PAID','CANCELLED')),
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, payroll_run_id) REFERENCES hr.payroll_runs(company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, employee_party_id) REFERENCES party.parties(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);
-- An employee is paid once per month, even after a transfer between stores.
CREATE UNIQUE INDEX ux_payroll_employee_month ON hr.payroll_lines (company_id, employee_party_id, period_month)
    WHERE status <> 'CANCELLED';
CREATE TRIGGER trg_touch BEFORE UPDATE ON hr.payroll_lines FOR EACH ROW EXECUTE FUNCTION core.touch_row();

ALTER TABLE hr.commission_entries ADD CONSTRAINT commission_entries_payroll_fk
    FOREIGN KEY (company_id, payroll_line_id) REFERENCES hr.payroll_lines(company_id, id);

CREATE TABLE hr.payroll_adjustments (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    employee_party_id   uuid NOT NULL,
    period_month        date NOT NULL CHECK (EXTRACT(DAY FROM period_month) = 1),
    adjustment_type     text NOT NULL CHECK (adjustment_type IN ('INCENTIVE','DEDUCTION','ADVANCE')),
    amount              core.money NOT NULL CHECK (amount > 0),
    reason              text,
    payroll_line_id     uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, employee_party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, payroll_line_id) REFERENCES hr.payroll_lines(company_id, id)
);
CREATE INDEX ix_adjustments_emp ON hr.payroll_adjustments (company_id, employee_party_id, period_month);

-- Salary payments: append-only. Each payment is linked to one APPROVED payroll line and a journal entry.
CREATE TABLE hr.salary_payments (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,                  -- store of the payroll line
    payroll_line_id     uuid NOT NULL,
    employee_party_id   uuid NOT NULL,
    paid_on             date NOT NULL,
    amount              core.money NOT NULL CHECK (amount > 0),
    method              text NOT NULL CHECK (method IN ('CASH','BANK','OTHER')),
    reference           text,
    journal_entry_id    uuid,
    paid_by             uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, payroll_line_id) REFERENCES hr.payroll_lines(company_id, id),
    FOREIGN KEY (company_id, employee_party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, journal_entry_id) REFERENCES acct.journal_entries(company_id, id)
);
CREATE INDEX ix_salary_pay_line ON hr.salary_payments (company_id, payroll_line_id);

-- =====================================================================
-- EXPENSES (rent, utilities, bilty/freight, renovation, misc; salaries come through payroll)
-- store_id = '00000000-0000-0000-0000-000000000000'::uuid -> head-office expense; allocations can spread it over stores (only for store 0).
-- Workflow: DRAFT -> SUBMITTED -> APPROVED -> POSTED (journal), or SUBMITTED -> REJECTED.
-- Payment is recorded through acct.voucher_allocations (paid now or later).
-- =====================================================================
CREATE TABLE exp.expense_categories (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    code            text NOT NULL,
    name            text NOT NULL,
    gl_account_id   uuid NOT NULL,
    is_recurring    boolean NOT NULL DEFAULT false,
    is_active       boolean NOT NULL DEFAULT true,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, code),
    FOREIGN KEY (company_id, gl_account_id) REFERENCES acct.accounts(company_id, id)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON exp.expense_categories FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE exp.recurring_schedules (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,                  -- 0 = head office
    category_id         uuid NOT NULL,
    amount              core.money NOT NULL CHECK (amount > 0),
    day_of_month        smallint NOT NULL CHECK (day_of_month BETWEEN 1 AND 28),
    payee_party_id      uuid,
    starts_on           date NOT NULL,
    ends_on             date,
    next_due_on         date NOT NULL,
    is_active           boolean NOT NULL DEFAULT true,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CHECK (ends_on IS NULL OR ends_on >= starts_on),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, category_id) REFERENCES exp.expense_categories(company_id, id),
    FOREIGN KEY (company_id, payee_party_id) REFERENCES party.parties(company_id, id)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON exp.recurring_schedules FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TABLE exp.expenses (
    company_id              uuid NOT NULL REFERENCES platform.companies(id),
    id                      uuid NOT NULL DEFAULT uuidv7(),
    store_id                uuid NOT NULL,              -- 0 = head office
    category_id             uuid NOT NULL,
    expense_date            date NOT NULL,
    period_month            date NOT NULL CHECK (EXTRACT(DAY FROM period_month) = 1),
    amount                  core.money NOT NULL CHECK (amount > 0),
    payee_party_id          uuid,
    bill_ref                text,                         -- utility bill no., bilty no., etc.
    description             text,
    due_date                date,
    payment_method          text CHECK (payment_method IN ('CASH','BANK','CHEQUE','OTHER')),
    paid_amount             core.money NOT NULL DEFAULT 0 CHECK (paid_amount >= 0),
    payment_status          text NOT NULL DEFAULT 'UNPAID' CHECK (payment_status IN ('UNPAID','PARTIAL','PAID')),
    status                  text NOT NULL DEFAULT 'DRAFT' CHECK (status IN
                                ('DRAFT','SUBMITTED','APPROVED','REJECTED','POSTED','VOID')),
    submitted_by            uuid,
    submitted_at            timestamptz,
    approved_by             uuid,
    approved_at             timestamptz,
    rejected_reason         text,
    recurring_schedule_id   uuid,
    journal_entry_id        uuid,
    created_at              timestamptz NOT NULL DEFAULT now(),
    created_by              uuid,
    updated_at              timestamptz,
    updated_by              uuid,
    row_ver                 bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    CHECK (paid_amount <= amount),
    CHECK (status <> 'REJECTED' OR rejected_reason IS NOT NULL),
    CHECK (status NOT IN ('POSTED') OR (approved_by IS NOT NULL AND journal_entry_id IS NOT NULL)),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, category_id) REFERENCES exp.expense_categories(company_id, id),
    FOREIGN KEY (company_id, payee_party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, recurring_schedule_id) REFERENCES exp.recurring_schedules(company_id, id),
    FOREIGN KEY (company_id, journal_entry_id) REFERENCES acct.journal_entries(company_id, id)
);
CREATE INDEX ix_expenses_store_period ON exp.expenses (company_id, store_id, period_month) WHERE status = 'POSTED';
CREATE INDEX ix_expenses_cat_period   ON exp.expenses (company_id, category_id, period_month);
CREATE INDEX ix_expenses_due          ON exp.expenses (company_id, due_date) WHERE payment_status <> 'PAID' AND status = 'POSTED';
CREATE INDEX ix_expenses_queue        ON exp.expenses (company_id, status) WHERE status = 'SUBMITTED';
CREATE TRIGGER trg_touch BEFORE UPDATE ON exp.expenses FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- How a head-office expense is shared between stores. Sum must equal the expense (deferred check).
CREATE TABLE exp.expense_allocations (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    expense_id      uuid NOT NULL,
    store_id        uuid NOT NULL,
    amount          core.money NOT NULL CHECK (amount > 0),
    PRIMARY KEY (company_id, expense_id, store_id),
    FOREIGN KEY (company_id, expense_id) REFERENCES exp.expenses(company_id, id) ON DELETE CASCADE,
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);

ALTER TABLE acct.voucher_allocations ADD CONSTRAINT voucher_alloc_expense_fk
    FOREIGN KEY (company_id, expense_id) REFERENCES exp.expenses(company_id, id);

-- =====================================================================
-- OPERATIONS: exceptions for owner follow-up (not for price drift, which has its own table)
-- =====================================================================
CREATE TABLE ops.exceptions (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    store_id        uuid,
    kind            text NOT NULL CHECK (kind IN ('SHORTFALL','UNMAPPED_ITEM','PUSH_FAILED','RECON_DIFF',
                                                   'TRANSFER_OVER_RECEIVED','CREDIT_LIMIT_OVERRIDE')),
    ref_type        text,
    ref_id          uuid,
    qty             core.qty,
    details         jsonb NOT NULL DEFAULT '{}'::jsonb,
    status          text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','RESOLVED')),
    created_at      timestamptz NOT NULL DEFAULT now(),
    resolved_at     timestamptz,
    resolved_by     uuid,
    resolution      text,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id)
);
CREATE INDEX ix_exceptions_open ON ops.exceptions (company_id, status) WHERE status = 'OPEN';

-- =====================================================================
-- REPORTING ROLLUPS (rebuilt by worker from a dirty queue; the owner's default view is the last 7 days)
-- =====================================================================
CREATE TABLE rpt.dirty_queue (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    store_id        uuid NOT NULL,
    sale_date       date NOT NULL,
    queued_at       timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, store_id, sale_date)
);

CREATE TABLE rpt.sales_store_day (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    store_id        uuid NOT NULL,
    sale_date       date NOT NULL,
    channel_id      uuid NOT NULL,
    invoices        int NOT NULL DEFAULT 0,
    qty             core.qty NOT NULL DEFAULT 0,
    gross_amount    core.money NOT NULL DEFAULT 0,
    discount_amount core.money NOT NULL DEFAULT 0,
    tax_amount      core.money NOT NULL DEFAULT 0,
    net_amount      core.money NOT NULL DEFAULT 0,
    cost_amount     core.money NOT NULL DEFAULT 0,
    PRIMARY KEY (company_id, store_id, sale_date, channel_id)
);

-- Partitioned by sale_date. price_band_id 0 = unbanded.
CREATE TABLE rpt.sales_variant_day (
    company_id      uuid NOT NULL,
    store_id        uuid NOT NULL,
    sale_date       date NOT NULL,
    channel_id      uuid NOT NULL,
    variant_id      uuid NOT NULL,
    price_band_id   uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'::uuid,
    product_id      uuid NOT NULL,
    category_id     uuid NOT NULL,
    department_id   uuid NOT NULL,
    brand_id        uuid,
    season_id       uuid,
    gender_id       uuid,
    color_id        uuid,
    size_group_id   uuid,
    product_type_id uuid,
    qty             core.qty NOT NULL DEFAULT 0,
    list_amount     core.money NOT NULL DEFAULT 0,
    discount_amount core.money NOT NULL DEFAULT 0,
    taxable_amount  core.money NOT NULL DEFAULT 0,
    tax_amount      core.money NOT NULL DEFAULT 0,
    net_amount      core.money NOT NULL DEFAULT 0,
    cost_amount     core.money NOT NULL DEFAULT 0,
    PRIMARY KEY (company_id, store_id, sale_date, channel_id, variant_id, price_band_id)
) PARTITION BY RANGE (sale_date);
CREATE INDEX ix_svd_variant ON rpt.sales_variant_day (company_id, variant_id, sale_date);
CREATE INDEX ix_svd_band    ON rpt.sales_variant_day (company_id, price_band_id, sale_date);
CREATE INDEX ix_svd_product ON rpt.sales_variant_day (company_id, product_id, sale_date);

CREATE TABLE rpt.article_velocity (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    store_id        uuid NOT NULL,
    variant_id      uuid NOT NULL,
    window_days     int NOT NULL CHECK (window_days IN (30,60,90)),
    units_sold      core.qty NOT NULL DEFAULT 0,
    units_per_day  numeric(14,4) NOT NULL DEFAULT 0,
    on_hand         core.qty NOT NULL DEFAULT 0,
    days_of_cover  numeric(14,2),                 -- NULL when velocity is zero
    computed_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, store_id, variant_id, window_days)
);

CREATE TABLE rpt.stock_month_snapshot (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    store_id        uuid NOT NULL,
    variant_id      uuid NOT NULL,
    month_end       date NOT NULL,
    qty_on_hand     core.qty NOT NULL,
    avg_cost        core.unit_cost NOT NULL,
    stock_value     core.money NOT NULL,
    PRIMARY KEY (company_id, store_id, variant_id, month_end)
);

CREATE TABLE rpt.saved_reports (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    owner_user_id   uuid NOT NULL,
    name            text NOT NULL,
    definition      jsonb NOT NULL,                -- whitelisted dimensions, measures, filters, sort
    is_shared       boolean NOT NULL DEFAULT false,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid,
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, owner_user_id) REFERENCES sec.users(company_id, id)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON rpt.saved_reports FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- =====================================================================
-- AUDIT: append-only activity log (who, when, store, old and new values)
-- =====================================================================
CREATE TABLE audit.activity_log (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    occurred_at     timestamptz NOT NULL DEFAULT now(),
    user_id         uuid,
    store_id        uuid,
    device_id       uuid,
    action          text NOT NULL,
    entity_type     text NOT NULL,
    entity_id       text,
    old_values      jsonb,
    new_values      jsonb,
    remarks         text,
    PRIMARY KEY (company_id, id)
);
CREATE INDEX ix_audit_time ON audit.activity_log USING brin (occurred_at);
CREATE INDEX ix_audit_entity ON audit.activity_log (company_id, entity_type, entity_id);

-- =====================================================================
-- DEFERRED FOREIGN KEYS (tables that reference each other across sections)
-- =====================================================================
ALTER TABLE acct.voucher_allocations ADD CONSTRAINT voucher_alloc_sale_fk
    FOREIGN KEY (company_id, sale_invoice_id) REFERENCES sales.invoices(company_id, id);
ALTER TABLE acct.voucher_allocations ADD CONSTRAINT voucher_alloc_purchase_fk
    FOREIGN KEY (company_id, purchase_invoice_id) REFERENCES purch.invoices(company_id, id);
ALTER TABLE acct.voucher_allocations ADD CONSTRAINT voucher_alloc_payroll_fk
    FOREIGN KEY (company_id, payroll_line_id) REFERENCES hr.payroll_lines(company_id, id);
ALTER TABLE integ.stock_reservations ADD CONSTRAINT stock_reservations_variant_fk
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id);

-- =====================================================================
-- V5 ADDITIONS: product claims, item-level transfer scans, vendor articles, promotion usage
-- =====================================================================

-- Product claims (damaged / defective / lost pieces). Approval removes stock and posts the loss.
CREATE TABLE inv.claims (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    claim_no            bigint NOT NULL,
    claim_date          date NOT NULL,
    variant_id          uuid NOT NULL,
    qty                 core.qty NOT NULL CHECK (qty > 0),
    reason              text NOT NULL CHECK (reason IN ('DAMAGED','DEFECTIVE','LOST','OTHER')),
    note                text,
    status              text NOT NULL DEFAULT 'REQUESTED' CHECK (status IN ('REQUESTED','APPROVED','REJECTED')),
    requested_by        uuid,
    requested_at        timestamptz NOT NULL DEFAULT now(),
    decided_by          uuid,
    decided_at          timestamptz,
    reject_reason       text,
    unit_cost           core.unit_cost,
    journal_entry_id    uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, store_id, claim_no),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CHECK (status <> 'APPROVED' OR (decided_by IS NOT NULL AND journal_entry_id IS NOT NULL)),
    CHECK (status <> 'REJECTED' OR reject_reason IS NOT NULL),
    FOREIGN KEY (company_id, store_id)   REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    FOREIGN KEY (company_id, journal_entry_id) REFERENCES acct.journal_entries(company_id, id)
);
CREATE INDEX ix_claims_status ON inv.claims (company_id, store_id, status);
CREATE TRIGGER trg_touch BEFORE UPDATE ON inv.claims FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- Item-level scans for transfers. One row per scanned piece.
--   SEND    : sending store scans every piece while the transfer is DRAFT (drives transfer_lines.qty_scanned)
--   RECEIVE : receiving store scans every piece it gets (drives transfer_lines.qty_received)
-- device_scan_uuid is generated by the scanner app: a retried or duplicated offline scan is rejected.
CREATE TABLE inv.transfer_scans (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    transfer_id         uuid NOT NULL,
    store_id            uuid NOT NULL,                  -- store that scanned
    phase               text NOT NULL CHECK (phase IN ('SEND','RECEIVE')),
    barcode             text NOT NULL,
    variant_id          uuid,                           -- resolved from barcode by trigger
    device_scan_uuid    uuid NOT NULL,
    scanned_by          uuid,
    scanned_at          timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, device_scan_uuid),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    FOREIGN KEY (company_id, transfer_id) REFERENCES inv.transfers(company_id, id),
    FOREIGN KEY (company_id, store_id)    REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, variant_id)  REFERENCES catalog.variants(company_id, id)
);
CREATE INDEX ix_scans_transfer ON inv.transfer_scans (company_id, transfer_id, phase);

-- Vendor-to-article mapping: many vendors per article, many articles per vendor.
CREATE TABLE purch.vendor_articles (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    vendor_party_id     uuid NOT NULL,
    product_id          uuid NOT NULL,
    vendor_article_code text,                             -- the supplier's own code for the article
    purchase_cost       core.money CHECK (purchase_cost IS NULL OR purchase_cost >= 0),
    moq                 core.qty NOT NULL DEFAULT 1 CHECK (moq > 0),   -- minimum order quantity (pieces)
    lead_time_days      int CHECK (lead_time_days IS NULL OR lead_time_days >= 0),
    is_preferred        boolean NOT NULL DEFAULT false,
    is_active           boolean NOT NULL DEFAULT true,
    note                text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, vendor_party_id, product_id),
    FOREIGN KEY (company_id, vendor_party_id) REFERENCES party.parties(company_id, id),
    FOREIGN KEY (company_id, product_id)      REFERENCES catalog.products(company_id, id)
);
CREATE UNIQUE INDEX ux_vendor_article_code ON purch.vendor_articles (company_id, vendor_party_id, vendor_article_code)
    WHERE vendor_article_code IS NOT NULL AND is_active;
CREATE UNIQUE INDEX ux_vendor_preferred ON purch.vendor_articles (company_id, product_id)
    WHERE is_preferred AND is_active;
CREATE TRIGGER trg_touch BEFORE UPDATE ON purch.vendor_articles FOR EACH ROW EXECUTE FUNCTION core.touch_row();

-- Promotion usage per store, day, channel and rule (rule 0 = no rule: manual or none).
CREATE TABLE rpt.promo_usage_day (
    company_id          uuid NOT NULL,
    store_id            uuid NOT NULL,
    sale_date           date NOT NULL,
    channel_id          uuid NOT NULL,
    rule_id             uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'::uuid,
    discount_source     text NOT NULL,
    lines               int NOT NULL DEFAULT 0,
    qty                 core.qty NOT NULL DEFAULT 0,
    list_amount         core.money NOT NULL DEFAULT 0,
    discount_amount     core.money NOT NULL DEFAULT 0,
    net_amount          core.money NOT NULL DEFAULT 0,
    PRIMARY KEY (company_id, store_id, sale_date, channel_id, rule_id, discount_source)
);

-- Sale and return lines: a return must name the sale line it reverses (commission and stock trace).
ALTER TABLE sales.invoice_lines ADD CONSTRAINT invoice_lines_original_fk
    FOREIGN KEY (company_id, original_line_id, original_line_sale_date)
    REFERENCES sales.invoice_lines(company_id, id, sale_date);

-- Stock ledger: a transfer, claim or opening move is written once per store, article and document.
CREATE UNIQUE INDEX ux_ledger_doc_once ON inv.stock_ledger (company_id, store_id, variant_id, doc_type, doc_id, txn_date)
    WHERE doc_type IN ('OPENING','CLAIM','TRANSFER_OUT','TRANSFER_IN','TRANSFER_FOUND');

-- =====================================================================
-- ACCOUNTING FUNCTIONS AND TRIGGERS
-- =====================================================================
-- Every journal entry must have at least two lines and total debit = total credit.
-- Deferred: runs at COMMIT, so lines inserted after the header in the same transaction are seen.
CREATE OR REPLACE FUNCTION acct.check_entry_balanced() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_lines bigint;
    v_diff  bigint;
BEGIN
    SELECT count(*), COALESCE(sum(debit), 0) - COALESCE(sum(credit), 0)
      INTO v_lines, v_diff
      FROM acct.journal_lines
     WHERE company_id = NEW.company_id AND entry_id = NEW.id AND entry_date = NEW.entry_date;
    IF v_lines < 2 OR v_diff <> 0 THEN
        RAISE EXCEPTION 'journal entry % is not balanced (lines %, difference %)', NEW.id, v_lines, v_diff
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NULL;
END $$;

CREATE OR REPLACE FUNCTION acct.check_period_open() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM acct.periods
                WHERE company_id = NEW.company_id
                  AND period_start = date_trunc('month', NEW.entry_date)::date
                  AND status = 'CLOSED') THEN
        RAISE EXCEPTION 'accounting period containing % is closed', NEW.entry_date
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

-- Keeps account_month_balances and party_balances in step with every journal line.
CREATE OR REPLACE FUNCTION acct.apply_line_to_balances() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO acct.account_month_balances (company_id, store_id, account_id, period_start, debit, credit)
    VALUES (NEW.company_id, NEW.store_id, NEW.account_id,
            date_trunc('month', NEW.entry_date)::date, NEW.debit, NEW.credit)
    ON CONFLICT (company_id, store_id, account_id, period_start)
    DO UPDATE SET debit  = acct.account_month_balances.debit  + EXCLUDED.debit,
                  credit = acct.account_month_balances.credit + EXCLUDED.credit;

    IF NEW.party_id IS NOT NULL THEN
        INSERT INTO party.party_balances (company_id, party_id, debit_total, credit_total, last_entry_date)
        VALUES (NEW.company_id, NEW.party_id, NEW.debit, NEW.credit, NEW.entry_date)
        ON CONFLICT (company_id, party_id)
        DO UPDATE SET debit_total     = party.party_balances.debit_total  + EXCLUDED.debit_total,
                      credit_total    = party.party_balances.credit_total + EXCLUDED.credit_total,
                      last_entry_date = GREATEST(party.party_balances.last_entry_date, EXCLUDED.last_entry_date),
                      updated_at      = now();
    END IF;
    RETURN NULL;
END $$;

-- Deferred check: allocations of a head-office expense must add up to its amount.
CREATE OR REPLACE FUNCTION exp.check_allocation_total() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_expense uuid := CASE WHEN TG_OP = 'DELETE' THEN OLD.expense_id ELSE NEW.expense_id END;
    v_company uuid := CASE WHEN TG_OP = 'DELETE' THEN OLD.company_id ELSE NEW.company_id END;
    v_sum     bigint;
    v_amount  bigint;
BEGIN
    SELECT amount INTO v_amount FROM exp.expenses WHERE company_id = v_company AND id = v_expense;
    SELECT COALESCE(sum(amount), 0) INTO v_sum
      FROM exp.expense_allocations WHERE company_id = v_company AND expense_id = v_expense;
    IF v_sum <> 0 AND v_sum <> v_amount THEN
        RAISE EXCEPTION 'allocations of expense % total % but the expense is %', v_expense, v_sum, v_amount
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NULL;
END $$;

-- Rule targets must match the rule's target type; ALL rules take no targets.
CREATE OR REPLACE FUNCTION promo.check_rule_target() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_type text;
BEGIN
    SELECT target_type INTO v_type FROM promo.rules WHERE company_id = NEW.company_id AND id = NEW.rule_id;
    IF v_type = 'ALL' THEN
        RAISE EXCEPTION 'rule % applies to all items and cannot have item targets', NEW.rule_id;
    END IF;
    IF (v_type = 'CATEGORY' AND NEW.category_id IS NULL)
    OR (v_type = 'PRODUCT'  AND NEW.product_id  IS NULL)
    OR (v_type = 'VARIANT'  AND NEW.variant_id  IS NULL) THEN
        RAISE EXCEPTION 'target column does not match the rule target type %', v_type;
    END IF;
    RETURN NEW;
END $$;

-- Deferred: a store-scoped rule must name its POS channel and cannot name an online channel.
CREATE OR REPLACE FUNCTION promo.check_rule_scope() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_company uuid := CASE WHEN TG_OP = 'DELETE' THEN OLD.company_id ELSE NEW.company_id END;
    v_rule    uuid := CASE WHEN TG_OP = 'DELETE' THEN OLD.rule_id    ELSE NEW.rule_id    END;
BEGIN
    IF EXISTS (SELECT 1 FROM promo.rule_stores WHERE company_id = v_company AND rule_id = v_rule) THEN
        IF NOT EXISTS (SELECT 1 FROM promo.rule_channels WHERE company_id = v_company AND rule_id = v_rule) THEN
            RAISE EXCEPTION 'store-scoped rule % must name its POS channel', v_rule USING ERRCODE = 'check_violation';
        END IF;
        IF EXISTS (SELECT 1 FROM promo.rule_channels rc
                     JOIN integ.channels c ON c.company_id = rc.company_id AND c.id = rc.channel_id
                    WHERE rc.company_id = v_company AND rc.rule_id = v_rule AND c.kind <> 'STORE_POS') THEN
            RAISE EXCEPTION 'store-scoped rule % cannot apply to an online channel: Shopify prices are channel-wide',
                v_rule USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NULL;
END $$;

-- A barcode is unique across variants AND deal bundles of the same company.
CREATE OR REPLACE FUNCTION catalog.check_barcode_free() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_TABLE_NAME = 'variants' THEN
        IF NOT NEW.is_deleted AND EXISTS (SELECT 1 FROM promo.bundles
                  WHERE company_id = NEW.company_id AND barcode = NEW.barcode AND NOT is_deleted) THEN
            RAISE EXCEPTION 'barcode % is already used by a deal bundle', NEW.barcode USING ERRCODE = 'unique_violation';
        END IF;
    ELSE
        IF NOT NEW.is_deleted AND EXISTS (SELECT 1 FROM catalog.variants
                  WHERE company_id = NEW.company_id AND barcode = NEW.barcode AND NOT is_deleted) THEN
            RAISE EXCEPTION 'barcode % is already used by an article', NEW.barcode USING ERRCODE = 'unique_violation';
        END IF;
    END IF;
    RETURN NEW;
END $$;

-- =====================================================================
-- STOCK MOVEMENT PRIMITIVE
-- Every stock change goes through here: row lock, non-negative check, weighted average cost, ledger row.
-- Callers must lock multiple variants in ascending variant_id order (deadlock-free).
-- Returns the unit cost applied (average cost for outgoing quantities).
-- =====================================================================
CREATE OR REPLACE FUNCTION inv.post_stock_move(
    p_company   uuid,
    p_store     uuid,
    p_variant   uuid,
    p_qty       numeric,
    p_doc_type  text,
    p_doc_id    uuid,
    p_unit_cost numeric,
    p_txn_date  date
) RETURNS numeric
LANGUAGE plpgsql AS $$
DECLARE
    v_qty     numeric;
    v_avg     numeric;
    v_new_qty numeric;
    v_new_avg numeric;
    v_cost    numeric;
BEGIN
    IF p_qty IS NULL OR p_qty = 0 THEN
        RAISE EXCEPTION 'stock quantity must not be zero';
    END IF;

    INSERT INTO inv.stock_balances (company_id, store_id, variant_id)
    VALUES (p_company, p_store, p_variant)
    ON CONFLICT (company_id, store_id, variant_id) DO NOTHING;

    SELECT qty_on_hand, avg_cost INTO v_qty, v_avg
      FROM inv.stock_balances
     WHERE company_id = p_company AND store_id = p_store AND variant_id = p_variant
       FOR UPDATE;

    IF p_qty > 0 THEN
        v_new_qty := v_qty + p_qty;
        v_new_avg := ROUND((v_qty * v_avg + p_qty * COALESCE(p_unit_cost, 0)) / v_new_qty, 4);
        v_cost    := COALESCE(p_unit_cost, 0);
    ELSE
        IF v_qty + p_qty < 0 THEN
            RAISE EXCEPTION 'insufficient stock: store %, variant %, on hand %, requested %',
                p_store, p_variant, v_qty, -p_qty USING ERRCODE = 'check_violation';
        END IF;
        v_new_qty := v_qty + p_qty;
        v_new_avg := v_avg;
        v_cost    := v_avg;
    END IF;

    UPDATE inv.stock_balances
       SET qty_on_hand     = v_new_qty,
           avg_cost        = v_new_avg,
           last_receipt_at = CASE WHEN p_qty > 0 THEN now() ELSE last_receipt_at END,
           updated_at      = now()
     WHERE company_id = p_company AND store_id = p_store AND variant_id = p_variant;

    INSERT INTO inv.stock_ledger (company_id, store_id, variant_id, txn_date, doc_type, doc_id,
                                  qty, unit_cost, qty_after)
    VALUES (p_company, p_store, p_variant, p_txn_date, p_doc_type, p_doc_id,
            p_qty, v_cost, v_new_qty);

    RETURN v_cost;
END $$;

-- Ships a DRAFT transfer. All-or-nothing: if any line is short, nothing moves and every short line is reported.
-- Stock leaves the sender now; the receiver sees it as incoming until it is received.
-- Journal: Dr STOCK IN TRANSIT (head office), Cr STOCK (sending store), at average cost.
CREATE OR REPLACE FUNCTION inv.ship_transfer(p_company uuid, p_transfer uuid, p_user uuid)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    t         inv.transfers%ROWTYPE;
    r         record;
    v_short   text;
    v_cost    numeric;
    v_value   numeric := 0;
    v_date    date;
    v_entry   uuid;
    v_transit uuid;
    v_stock   uuid;
BEGIN
    SELECT (now() AT TIME ZONE c.timezone)::date INTO v_date FROM platform.companies c WHERE c.id = p_company;
    PERFORM set_config('app.internal_transfer_write', 'on', true);
    SELECT * INTO t FROM inv.transfers WHERE company_id = p_company AND id = p_transfer FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'transfer % not found', p_transfer;
    END IF;
    IF t.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'transfer % is %, only a DRAFT can be shipped', t.id, t.status;
    END IF;

    SELECT string_agg(format('variant %s: need %s, on hand %s', l.variant_id, l.qty_scanned,
                             COALESCE(b.qty_on_hand, 0)), '; ')
      INTO v_short
      FROM inv.transfer_lines l
      LEFT JOIN inv.stock_balances b
             ON b.company_id = l.company_id AND b.store_id = t.from_store_id AND b.variant_id = l.variant_id
     WHERE l.company_id = p_company AND l.transfer_id = p_transfer
       AND l.qty_scanned > COALESCE(b.qty_on_hand, 0);
    IF v_short IS NOT NULL THEN
        RAISE EXCEPTION 'transfer refused, short lines: %', v_short USING ERRCODE = 'check_violation';
    END IF;

    FOR r IN SELECT l.id, l.variant_id, l.qty_scanned
               FROM inv.transfer_lines l
              WHERE l.company_id = p_company AND l.transfer_id = p_transfer AND l.qty_scanned > 0
              ORDER BY l.variant_id
    LOOP
        v_cost := inv.post_stock_move(p_company, t.from_store_id, r.variant_id, -r.qty_scanned,
                                      'TRANSFER_OUT', t.id, NULL, v_date);
        UPDATE inv.transfer_lines
           SET qty_shipped = r.qty_scanned, unit_cost = v_cost
         WHERE company_id = p_company AND id = r.id;
        v_value := v_value + r.qty_scanned * v_cost;
    END LOOP;

    UPDATE inv.transfers
       SET status = 'SHIPPED', shipped_by = p_user, shipped_at = now()
     WHERE company_id = p_company AND id = p_transfer;

    INSERT INTO inv.transfer_events (company_id, transfer_id, event_type, user_id)
    VALUES (p_company, p_transfer, 'SHIP', p_user);

    IF v_value > 0 THEN
        SELECT id INTO v_transit FROM acct.accounts
         WHERE company_id = p_company AND store_id = '00000000-0000-0000-0000-000000000000'::uuid AND system_role = 'TRANSIT';
        SELECT id INTO v_stock FROM acct.accounts
         WHERE company_id = p_company AND store_id = t.from_store_id AND system_role = 'STOCK';
        INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
        VALUES (p_company, v_date, t.from_store_id, 'TRANSFER', t.id,
                'Transfer ' || t.transfer_no || ' shipped', p_user)
        RETURNING id INTO v_entry;
        INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, debit, credit)
        VALUES (p_company, v_entry, v_date, 1, '00000000-0000-0000-0000-000000000000'::uuid, v_transit, core.round_money(p_company, v_value), 0),
               (p_company, v_entry, v_date, 2, t.from_store_id, v_stock, 0, core.round_money(p_company, v_value));
    END IF;
END $$;

-- =====================================================================
-- PRICING ENGINE: rebuilds promo.effective_prices for one company.
-- Run after any rule, target, scope, price or stock change, and at each rule start/end time
-- (the scheduler reads the minimum valid_until). Scope may be narrowed later for speed.
--  - Each (channel, store, variant) pair gets the list price unless rules apply.
--  - One rule applies: sole candidate -> applied.
--  - Two or more candidates -> CONFLICT: sold at list price until an admin confirms one rule
--    (promo.conflict_groups). The confirmation is kept while the candidate set is unchanged.
--  - Store-scoped rules only reach POS channels. Shopify uses store 0 (channel-wide).
-- =====================================================================
CREATE OR REPLACE FUNCTION promo.rebuild_effective_prices(p_company uuid) RETURNS bigint
LANGUAGE plpgsql AS $$
DECLARE
    v_now  timestamptz := now();
    v_rows bigint;
    v_del  bigint;
BEGIN
    DROP TABLE IF EXISTS tmp_pairs, tmp_cand, tmp_priced, tmp_pair_state, tmp_final;

    -- 1. every sellable (channel, store, variant) with its list price and current stock
    CREATE TEMP TABLE tmp_pairs ON COMMIT DROP AS
    SELECT c.id AS channel_id, s.id AS store_id, v.id AS variant_id, p.id AS product_id, p.category_id,
           CASE WHEN c.kind = 'STORE_POS' THEN COALESCE(o.sale_price, vp.sale_price)
                ELSE COALESCE(ch.price, vp.sale_price) END AS list_price,
           (SELECT COALESCE(SUM(b.qty_on_hand), 0) FROM inv.stock_balances b
             WHERE b.company_id = c.company_id AND b.variant_id = v.id
               AND (c.kind = 'ONLINE' OR b.store_id = s.id)) AS stock_qty
      FROM integ.channels c
      JOIN core.stores s ON s.company_id = c.company_id
           AND ((c.kind = 'STORE_POS' AND s.id <> '00000000-0000-0000-0000-000000000000'::uuid AND s.is_active) OR (c.kind = 'ONLINE' AND s.id = '00000000-0000-0000-0000-000000000000'::uuid))
      JOIN catalog.variants v ON v.company_id = c.company_id AND NOT v.is_deleted
      JOIN catalog.products p ON p.company_id = v.company_id AND p.id = v.product_id AND NOT p.is_deleted
      JOIN catalog.variant_prices vp ON vp.company_id = v.company_id AND vp.variant_id = v.id
      LEFT JOIN catalog.store_price_overrides o
             ON o.company_id = s.company_id AND o.store_id = s.id AND o.variant_id = v.id
      LEFT JOIN integ.channel_prices ch
             ON ch.company_id = c.company_id AND ch.channel_id = c.id AND ch.variant_id = v.id
     WHERE c.company_id = p_company AND c.is_active;

    -- 2. every published, live rule that covers each pair
    CREATE TEMP TABLE tmp_cand ON COMMIT DROP AS
    SELECT pr.channel_id, pr.store_id, pr.variant_id, pr.list_price,
           r.id AS rule_id, r.ends_at, r.priority, r.discount_type,
           COALESCE(rt.discount_override, r.discount_value) AS disc_value,
           CASE r.target_type WHEN 'VARIANT' THEN 3 WHEN 'PRODUCT' THEN 2 WHEN 'CATEGORY' THEN 1 ELSE 0 END AS specificity
      FROM tmp_pairs pr
      JOIN promo.rules r
        ON r.company_id = p_company AND r.status = 'PUBLISHED' AND NOT r.is_deleted
       AND r.starts_at <= v_now AND (r.ends_at IS NULL OR r.ends_at > v_now)
       AND (r.stock_min IS NULL OR pr.stock_qty >= r.stock_min)
       AND (r.stock_max IS NULL OR pr.stock_qty <= r.stock_max)
      LEFT JOIN promo.rule_targets rt
        ON rt.company_id = r.company_id AND rt.rule_id = r.id
       AND ((r.target_type = 'CATEGORY' AND rt.category_id = pr.category_id)
         OR (r.target_type = 'PRODUCT'  AND rt.product_id  = pr.product_id)
         OR (r.target_type = 'VARIANT'  AND rt.variant_id  = pr.variant_id))
     WHERE (r.target_type = 'ALL' OR rt.id IS NOT NULL)
       AND (NOT EXISTS (SELECT 1 FROM promo.rule_channels rc
                         WHERE rc.company_id = r.company_id AND rc.rule_id = r.id)
         OR EXISTS (SELECT 1 FROM promo.rule_channels rc
                     WHERE rc.company_id = r.company_id AND rc.rule_id = r.id AND rc.channel_id = pr.channel_id))
       AND (NOT EXISTS (SELECT 1 FROM promo.rule_stores rs
                         WHERE rs.company_id = r.company_id AND rs.rule_id = r.id)
         OR EXISTS (SELECT 1 FROM promo.rule_stores rs
                     WHERE rs.company_id = r.company_id AND rs.rule_id = r.id AND rs.store_id = pr.store_id));

    -- 3. price each candidate, rounded per company setting, never above list
    CREATE TEMP TABLE tmp_priced ON COMMIT DROP AS
    SELECT c.channel_id, c.store_id, c.variant_id, c.rule_id, c.ends_at,
           LEAST(c.list_price, core.round_money(p_company,
               CASE c.discount_type
                   WHEN 'PERCENT'     THEN c.list_price * (100 - c.disc_value) / 100.0
                   WHEN 'AMOUNT'      THEN GREATEST(c.list_price - c.disc_value, 0)
                   ELSE c.disc_value
               END)) AS sale_price,
           c.list_price, c.disc_value, c.discount_type
      FROM tmp_cand c;

    -- 4. candidate count and key per pair
    CREATE TEMP TABLE tmp_pair_state ON COMMIT DROP AS
    SELECT channel_id, store_id, variant_id,
           COUNT(*) AS n,
           string_agg(rule_id::text, ',' ORDER BY rule_id) AS cand_key,
           MIN(ends_at) AS min_end
      FROM tmp_priced
     GROUP BY channel_id, store_id, variant_id;

    -- 5. conflict groups: retire any whose candidate set has changed or disappeared
    UPDATE promo.conflict_groups g
       SET status = 'SUPERSEDED'
     WHERE g.company_id = p_company AND g.status IN ('OPEN','RESOLVED')
       AND NOT EXISTS (SELECT 1 FROM tmp_pair_state s
                        WHERE s.channel_id = g.channel_id AND s.store_id = g.store_id
                          AND s.variant_id = g.variant_id AND s.n >= 2 AND s.cand_key = g.candidate_key);

    -- 6. open a group for each conflict that has no active group with the same key
    INSERT INTO promo.conflict_groups (company_id, channel_id, store_id, variant_id, candidate_key, status)
    SELECT p_company, s.channel_id, s.store_id, s.variant_id, s.cand_key, 'OPEN'
      FROM tmp_pair_state s
     WHERE s.n >= 2
       AND NOT EXISTS (SELECT 1 FROM promo.conflict_groups g
                        WHERE g.company_id = p_company AND g.channel_id = s.channel_id
                          AND g.store_id = s.store_id AND g.variant_id = s.variant_id
                          AND g.candidate_key = s.cand_key AND g.status IN ('OPEN','RESOLVED'));

    -- 7. candidate rows for open groups
    DELETE FROM promo.conflict_candidates cc
     WHERE cc.company_id = p_company
       AND cc.group_id IN (SELECT id FROM promo.conflict_groups WHERE company_id = p_company AND status = 'OPEN');

    INSERT INTO promo.conflict_candidates (company_id, group_id, rule_id, sale_price, discount_percent)
    SELECT p_company, g.id, tp.rule_id, tp.sale_price,
           CASE WHEN tp.list_price > 0 THEN ROUND((tp.list_price - tp.sale_price) * 100.0 / tp.list_price, 4) END
      FROM promo.conflict_groups g
      JOIN tmp_priced tp ON tp.channel_id = g.channel_id AND tp.store_id = g.store_id AND tp.variant_id = g.variant_id
     WHERE g.company_id = p_company AND g.status = 'OPEN';

    -- 8. the price each pair should show now
    CREATE TEMP TABLE tmp_final ON COMMIT DROP AS
    SELECT pr.channel_id, pr.store_id, pr.variant_id, pr.list_price,
           CASE WHEN COALESCE(s.n, 0) = 1 THEN tp.sale_price
                WHEN s.n >= 2 AND g.status = 'RESOLVED' THEN ch.sale_price
                ELSE pr.list_price END AS sale_price,
           CASE WHEN COALESCE(s.n, 0) = 1 THEN tp.rule_id
                WHEN s.n >= 2 AND g.status = 'RESOLVED' THEN ch.rule_id END AS rule_id,
           (COALESCE(s.n, 0) >= 2 AND COALESCE(g.status, '') <> 'RESOLVED') AS is_conflict,
           CASE WHEN s.n >= 2 THEN g.id END AS conflict_group_id,
           s.min_end AS valid_until
      FROM tmp_pairs pr
      LEFT JOIN tmp_pair_state s
             ON s.channel_id = pr.channel_id AND s.store_id = pr.store_id AND s.variant_id = pr.variant_id
      LEFT JOIN tmp_priced tp
             ON s.n = 1 AND tp.channel_id = s.channel_id AND tp.store_id = s.store_id AND tp.variant_id = s.variant_id
      LEFT JOIN promo.conflict_groups g
             ON s.n >= 2 AND g.company_id = p_company AND g.channel_id = pr.channel_id
            AND g.store_id = pr.store_id AND g.variant_id = pr.variant_id
            AND g.candidate_key = s.cand_key AND g.status IN ('OPEN','RESOLVED')
      LEFT JOIN tmp_priced ch
             ON g.status = 'RESOLVED' AND ch.channel_id = g.channel_id AND ch.store_id = g.store_id
            AND ch.variant_id = g.variant_id AND ch.rule_id = g.chosen_rule_id;

    -- 9. upsert the result and drop pairs that are no longer sellable
    INSERT INTO promo.effective_prices AS e
        (company_id, channel_id, store_id, variant_id, list_price, sale_price, discount_amount,
         discount_percent, rule_id, is_conflict, conflict_group_id, valid_from, valid_until, computed_at)
    SELECT p_company, f.channel_id, f.store_id, f.variant_id, f.list_price, f.sale_price,
           f.list_price - f.sale_price,
           CASE WHEN f.list_price > 0 THEN ROUND((f.list_price - f.sale_price) * 100.0 / f.list_price, 4) END,
           f.rule_id, f.is_conflict, f.conflict_group_id, v_now, f.valid_until, v_now
      FROM tmp_final f
    ON CONFLICT (company_id, channel_id, store_id, variant_id) DO UPDATE
       SET list_price = EXCLUDED.list_price, sale_price = EXCLUDED.sale_price,
           discount_amount = EXCLUDED.discount_amount, discount_percent = EXCLUDED.discount_percent,
           rule_id = EXCLUDED.rule_id, is_conflict = EXCLUDED.is_conflict,
           conflict_group_id = EXCLUDED.conflict_group_id, valid_from = EXCLUDED.valid_from,
           valid_until = EXCLUDED.valid_until, computed_at = EXCLUDED.computed_at;
    GET DIAGNOSTICS v_rows = ROW_COUNT;            -- rows written (inserted or updated)

    DELETE FROM promo.effective_prices e
     WHERE e.company_id = p_company
       AND NOT EXISTS (SELECT 1 FROM tmp_final f
                        WHERE f.channel_id = e.channel_id AND f.store_id = e.store_id AND f.variant_id = e.variant_id);
    GET DIAGNOSTICS v_del = ROW_COUNT;             -- rows removed

    RETURN v_rows + v_del;
END $$;

-- =====================================================================
-- REPORTING ROLLUPS
-- Sales write a row to rpt.dirty_queue; a worker rebuilds that store-day (idempotent).
-- =====================================================================
CREATE OR REPLACE FUNCTION rpt.mark_dirty() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO rpt.dirty_queue (company_id, store_id, sale_date, queued_at)
    VALUES (NEW.company_id, NEW.store_id, NEW.sale_date, now())
    ON CONFLICT (company_id, store_id, sale_date) DO UPDATE SET queued_at = now();
    RETURN NULL;
END $$;

-- Rollups count POSTED invoices only (a voided sale no longer inflates reports).
CREATE OR REPLACE FUNCTION rpt.refresh_store_day(p_company uuid, p_store uuid, p_date date)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
DELETE FROM rpt.sales_variant_day
 WHERE company_id = p_company AND store_id = p_store AND sale_date = p_date;

INSERT INTO rpt.sales_variant_day (company_id, store_id, sale_date, channel_id, variant_id, price_band_id,
    product_id, category_id, department_id, brand_id, season_id, gender_id, color_id, size_group_id,
    product_type_id, qty, list_amount, discount_amount, taxable_amount, tax_amount, net_amount, cost_amount)
SELECT l.company_id, l.store_id, l.sale_date, l.channel_id, l.variant_id, COALESCE(l.price_band_id, '00000000-0000-0000-0000-000000000000'::uuid),
    (array_agg(l.product_id))[1], (array_agg(l.category_id))[1], (array_agg(l.department_id))[1], (array_agg(l.brand_id))[1], (array_agg(l.season_id))[1],
    (array_agg(l.gender_id))[1], (array_agg(l.color_id))[1], (array_agg(l.size_group_id))[1], (array_agg(l.product_type_id))[1],
    SUM(l.qty), SUM(l.list_price * l.qty), SUM(l.discount_amount), SUM(l.taxable_amount),
    SUM(l.tax_amount), SUM(l.net_amount), SUM(l.cost_amount)
  FROM sales.invoice_lines l
  JOIN sales.invoices i ON i.company_id = l.company_id AND i.id = l.invoice_id AND i.status = 'POSTED'
 WHERE l.company_id = p_company AND l.store_id = p_store AND l.sale_date = p_date
 GROUP BY l.company_id, l.store_id, l.sale_date, l.channel_id, l.variant_id, COALESCE(l.price_band_id, '00000000-0000-0000-0000-000000000000'::uuid);

DELETE FROM rpt.sales_store_day
 WHERE company_id = p_company AND store_id = p_store AND sale_date = p_date;

INSERT INTO rpt.sales_store_day (company_id, store_id, sale_date, channel_id, invoices, qty,
    gross_amount, discount_amount, tax_amount, net_amount, cost_amount)
SELECT l.company_id, l.store_id, l.sale_date, l.channel_id,
    COUNT(DISTINCT l.invoice_id) FILTER (WHERE l.doc_type = 'SALE'),
    SUM(l.qty), SUM(l.list_price * l.qty), SUM(l.discount_amount), SUM(l.tax_amount),
    SUM(l.net_amount), SUM(l.cost_amount)
  FROM sales.invoice_lines l
  JOIN sales.invoices i ON i.company_id = l.company_id AND i.id = l.invoice_id AND i.status = 'POSTED'
 WHERE l.company_id = p_company AND l.store_id = p_store AND l.sale_date = p_date
 GROUP BY l.company_id, l.store_id, l.sale_date, l.channel_id;

DELETE FROM rpt.promo_usage_day
 WHERE company_id = p_company AND store_id = p_store AND sale_date = p_date;

INSERT INTO rpt.promo_usage_day (company_id, store_id, sale_date, channel_id, rule_id, discount_source,
    lines, qty, list_amount, discount_amount, net_amount)
SELECT l.company_id, l.store_id, l.sale_date, l.channel_id, COALESCE(l.rule_id, '00000000-0000-0000-0000-000000000000'::uuid), l.discount_source,
    COUNT(*), SUM(l.qty), SUM(l.list_price * l.qty), SUM(l.discount_amount), SUM(l.net_amount)
  FROM sales.invoice_lines l
  JOIN sales.invoices i ON i.company_id = l.company_id AND i.id = l.invoice_id AND i.status = 'POSTED'
 WHERE l.company_id = p_company AND l.store_id = p_store AND l.sale_date = p_date
 GROUP BY l.company_id, l.store_id, l.sale_date, l.channel_id, COALESCE(l.rule_id, '00000000-0000-0000-0000-000000000000'::uuid), l.discount_source;
END $$;


-- Processes up to p_limit queued store-days. SKIP LOCKED lets several workers run in parallel.
CREATE OR REPLACE FUNCTION rpt.process_dirty_queue(p_company uuid, p_limit int DEFAULT 200)
RETURNS int LANGUAGE plpgsql AS $$
DECLARE
    r record;
    n int := 0;
BEGIN
    FOR r IN SELECT store_id, sale_date FROM rpt.dirty_queue
              WHERE company_id = p_company ORDER BY queued_at
              LIMIT p_limit FOR UPDATE SKIP LOCKED
    LOOP
        PERFORM rpt.refresh_store_day(p_company, r.store_id, r.sale_date);
        DELETE FROM rpt.dirty_queue
         WHERE company_id = p_company AND store_id = r.store_id AND sale_date = r.sale_date;
        n := n + 1;
    END LOOP;
    RETURN n;
END $$;

-- =====================================================================
-- V5 FUNCTIONS
-- =====================================================================

-- Permission check for money-moving functions. COMPANY_OWNER has every permission.
CREATE OR REPLACE FUNCTION sec.require_permission(p_company uuid, p_user uuid, p_perm text)
RETURNS void LANGUAGE plpgsql STABLE AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sec.users u
         WHERE u.company_id = p_company AND u.id = p_user
           AND u.status = 'ACTIVE' AND NOT u.is_deleted
           AND (u.role_code = 'COMPANY_OWNER'
                OR EXISTS (SELECT 1 FROM sec.user_permissions up
                            WHERE up.company_id = u.company_id AND up.user_id = u.id
                              AND up.permission_code = p_perm))
    ) THEN
        RAISE EXCEPTION 'user % lacks permission %', p_user, p_perm USING ERRCODE = 'insufficient_privilege';
    END IF;
END $$;

-- Commission accrual: runs for every new invoice line.
--  RETURN line -> REVERSAL rows for every live EARN row of the original line.
--  SALE line   -> EARN rows for the line salesman (SALESMAN plans) and the store manager (MANAGER plans).
-- Plan choice per employee and role: members first, then the newest valid plan.
-- Rule choice per plan: PRODUCT, then CATEGORY, then PRICE_BAND, then priority. No rule = plan default.
-- Commission base: NET_SALE = line net amount, GROSS_MARGIN = net amount minus cost. Rounded per company rule.
CREATE OR REPLACE FUNCTION hr.accrue_line_commission() RETURNS trigger LANGUAGE plpgsql AS $$
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
            employee_party_id, plan_id, rule_id, entry_type, reverses_entry_id, calc_type, rate, fixed_amount,
            qty, base_amount, commission_amount, status)
        SELECT e.company_id, NEW.store_id, NEW.sale_date, NEW.invoice_id, NEW.id, NEW.sale_date,
               e.employee_party_id, e.plan_id, e.rule_id, 'REVERSAL', e.id, e.calc_type, e.rate, e.fixed_amount,
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
        SELECT NEW.salesman_party_id AS emp, 'SALESMAN'::text AS role
         WHERE NEW.salesman_party_id IS NOT NULL
        UNION ALL
        SELECT sp.party_id, 'MANAGER'::text
          FROM party.staff_profiles sp
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
                              AND m.employee_party_id = v_cand.emp))
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
            employee_party_id, plan_id, rule_id, entry_type, calc_type, rate, fixed_amount,
            qty, base_amount, commission_amount, status)
        VALUES (NEW.company_id, NEW.store_id, NEW.sale_date, NEW.invoice_id, NEW.id, NEW.sale_date,
            v_cand.emp, v_plan.id, v_rule.id, 'EARN', v_calc,
            CASE WHEN v_calc = 'PERCENT' THEN v_rate END,
            CASE WHEN v_calc = 'FIXED_PER_PIECE' THEN v_fixed END,
            NEW.qty, v_base, v_comm, 'PENDING');
    END LOOP;
    RETURN NULL;
END $$;

-- Voided invoice: every live EARN row of its lines is reversed (once).
CREATE OR REPLACE FUNCTION hr.reverse_invoice_commission() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO hr.commission_entries (company_id, store_id, sale_date, invoice_id, invoice_line_id, line_sale_date,
        employee_party_id, plan_id, rule_id, entry_type, reverses_entry_id, calc_type, rate, fixed_amount,
        qty, base_amount, commission_amount, status)
    SELECT e.company_id, e.store_id, NEW.sale_date, e.invoice_id, e.invoice_line_id, e.line_sale_date,
           e.employee_party_id, e.plan_id, e.rule_id, 'REVERSAL', e.id, e.calc_type, e.rate, e.fixed_amount,
           -e.qty, -e.base_amount, -e.commission_amount, 'PENDING'
      FROM hr.commission_entries e
     WHERE e.company_id = NEW.company_id AND e.invoice_id = NEW.id
       AND e.entry_type = 'EARN' AND e.status <> 'CANCELLED'
       AND NOT EXISTS (SELECT 1 FROM hr.commission_entries r
                        WHERE r.company_id = e.company_id AND r.reverses_entry_id = e.id);
    RETURN NULL;
END $$;

-- Approval of pending commission in a store and period. Manager plans wait until store sales reach min_store_sales.
CREATE OR REPLACE FUNCTION hr.approve_commissions(p_company uuid, p_store uuid, p_from date, p_to date, p_user uuid)
RETURNS int LANGUAGE plpgsql AS $$
DECLARE
    v_store_sales numeric;
    v_n int;
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'COMMISSION_APPROVE');
    SELECT COALESCE(SUM(l.net_amount), 0) INTO v_store_sales
      FROM sales.invoice_lines l
     WHERE l.company_id = p_company AND l.store_id = p_store AND l.sale_date BETWEEN p_from AND p_to;

    UPDATE hr.commission_entries e
       SET status = 'APPROVED', approved_by = p_user, approved_at = now()
     WHERE e.company_id = p_company AND e.store_id = p_store
       AND e.sale_date BETWEEN p_from AND p_to AND e.status = 'PENDING'
       AND (v_store_sales >= (SELECT p.min_store_sales FROM hr.commission_plans p
                               WHERE p.company_id = e.company_id AND p.id = e.plan_id)
            OR (SELECT p.applies_to FROM hr.commission_plans p
                 WHERE p.company_id = e.company_id AND p.id = e.plan_id) = 'SALESMAN');
    GET DIAGNOSTICS v_n = ROW_COUNT;
    RETURN v_n;
END $$;

-- Salary history: called on insert or change of a staff member's base salary or pay frequency.
CREATE OR REPLACE FUNCTION hr.log_salary_term() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_today date;
BEGIN
    IF TG_OP = 'UPDATE' AND NEW.base_salary = OLD.base_salary AND NEW.pay_frequency = OLD.pay_frequency THEN
        RETURN NULL;
    END IF;
    SELECT (now() AT TIME ZONE c.timezone)::date INTO v_today
      FROM platform.companies c WHERE c.id = NEW.company_id;
    INSERT INTO hr.salary_terms (company_id, employee_party_id, effective_from, base_salary, pay_frequency, changed_by)
    VALUES (NEW.company_id, NEW.party_id, v_today, NEW.base_salary, NEW.pay_frequency, core.current_user_id())
    ON CONFLICT (company_id, employee_party_id, effective_from) DO UPDATE
        SET base_salary = EXCLUDED.base_salary, pay_frequency = EXCLUDED.pay_frequency,
            changed_by = EXCLUDED.changed_by, changed_at = now();
    RETURN NULL;
END $$;

-- Builds (or rebuilds, while DRAFT) the payroll of one store for one month.
-- Base = salary term in force at month end. Commission = APPROVED commission not yet on a payroll.
-- Incentives and deductions come from hr.payroll_adjustments. Linked rows are reserved by payroll_line_id.
CREATE OR REPLACE FUNCTION hr.build_payroll_run(p_company uuid, p_store uuid, p_month date, p_user uuid)
RETURNS uuid LANGUAGE plpgsql AS $$
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

    INSERT INTO hr.payroll_lines (company_id, payroll_run_id, store_id, employee_party_id, period_month,
        base_salary, total_sales, commission_amount, incentive_amount, deduction_amount, status, created_by)
    SELECT sp.company_id, v_run.id, p_store, sp.party_id, v_from,
        COALESCE((SELECT t.base_salary FROM hr.salary_terms t
                   WHERE t.company_id = sp.company_id AND t.employee_party_id = sp.party_id
                     AND t.effective_from <= v_to ORDER BY t.effective_from DESC LIMIT 1), sp.base_salary),
        COALESCE((SELECT SUM(l.net_amount) FROM sales.invoice_lines l
                   WHERE l.company_id = sp.company_id AND l.salesman_party_id = sp.party_id
                     AND l.sale_date BETWEEN v_from AND v_to), 0),
        COALESCE((SELECT SUM(e.commission_amount) FROM hr.commission_entries e
                   WHERE e.company_id = sp.company_id AND e.employee_party_id = sp.party_id
                     AND e.status = 'APPROVED' AND e.payroll_line_id IS NULL
                     AND e.sale_date BETWEEN v_from AND v_to), 0),
        COALESCE((SELECT SUM(a.amount) FROM hr.payroll_adjustments a
                   WHERE a.company_id = sp.company_id AND a.employee_party_id = sp.party_id
                     AND a.period_month = v_from AND a.adjustment_type = 'INCENTIVE'
                     AND a.payroll_line_id IS NULL), 0),
        COALESCE((SELECT SUM(a.amount) FROM hr.payroll_adjustments a
                   WHERE a.company_id = sp.company_id AND a.employee_party_id = sp.party_id
                     AND a.period_month = v_from AND a.adjustment_type IN ('DEDUCTION','ADVANCE')
                     AND a.payroll_line_id IS NULL), 0),
        'DRAFT', p_user
      FROM party.staff_profiles sp
     WHERE sp.company_id = p_company AND sp.current_store_id = p_store
       AND (sp.left_on IS NULL OR sp.left_on >= v_from)
       AND (sp.joined_on IS NULL OR sp.joined_on <= v_to);

    -- Reserve the commission and adjustments that went into the new lines.
    UPDATE hr.commission_entries e SET payroll_line_id = pl.id
      FROM hr.payroll_lines pl
     WHERE pl.company_id = p_company AND pl.payroll_run_id = v_run.id AND pl.status = 'DRAFT'
       AND e.company_id = p_company AND e.employee_party_id = pl.employee_party_id
       AND e.status = 'APPROVED' AND e.payroll_line_id IS NULL
       AND e.sale_date BETWEEN v_from AND v_to;
    UPDATE hr.payroll_adjustments a SET payroll_line_id = pl.id
      FROM hr.payroll_lines pl
     WHERE pl.company_id = p_company AND pl.payroll_run_id = v_run.id AND pl.status = 'DRAFT'
       AND a.company_id = p_company AND a.employee_party_id = pl.employee_party_id
       AND a.period_month = v_from AND a.payroll_line_id IS NULL;
    RETURN v_run.id;
END $$;

-- Approves a DRAFT payroll: lines become APPROVED and the salary expense is posted.
-- Dr SALARY EXPENSE (store) / Cr SALARY PAYABLE (head office) for the net pay of the run.
CREATE OR REPLACE FUNCTION hr.approve_payroll_run(p_company uuid, p_run uuid, p_user uuid)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    v_run   hr.payroll_runs%ROWTYPE;
    v_total numeric;
    v_exp   uuid;
    v_pay   uuid;
    v_entry uuid;
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'PAYROLL_APPROVE');
    SELECT * INTO v_run FROM hr.payroll_runs WHERE company_id = p_company AND id = p_run FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'payroll run % not found', p_run; END IF;
    IF v_run.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'payroll run % is %, only a DRAFT can be approved', p_run, v_run.status USING ERRCODE = 'check_violation';
    END IF;
    SELECT COALESCE(SUM(net_pay), 0) INTO v_total FROM hr.payroll_lines
     WHERE company_id = p_company AND payroll_run_id = p_run AND status = 'DRAFT';
    SELECT id INTO v_exp FROM acct.accounts WHERE company_id = p_company AND store_id = v_run.store_id AND system_role = 'SALARY_EXPENSE';
    SELECT id INTO v_pay FROM acct.accounts WHERE company_id = p_company AND store_id = '00000000-0000-0000-0000-000000000000'::uuid AND system_role = 'SALARY_PAYABLE';

    INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
    VALUES (p_company, v_run.period_month, v_run.store_id, 'PAYROLL', p_run,
            'Salary ' || to_char(v_run.period_month, 'YYYY-MM'), p_user)
    RETURNING id INTO v_entry;
    IF v_total <> 0 THEN
        INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, debit, credit)
        VALUES (p_company, v_entry, v_run.period_month, 1, v_run.store_id, v_exp, core.round_money(p_company, v_total), 0),
               (p_company, v_entry, v_run.period_month, 2, '00000000-0000-0000-0000-000000000000'::uuid, v_pay, 0, core.round_money(p_company, v_total));
    END IF;

    UPDATE hr.payroll_lines SET status = 'APPROVED'
     WHERE company_id = p_company AND payroll_run_id = p_run AND status = 'DRAFT';
    UPDATE hr.payroll_runs SET status = 'APPROVED', approved_by = p_user, approved_at = now(), journal_entry_id = v_entry
     WHERE company_id = p_company AND id = p_run;
    RETURN v_entry;
END $$;

-- Applies a salary payment to an APPROVED line: checks amount, updates the line, closes its commission when paid in full.
CREATE OR REPLACE FUNCTION hr.apply_salary_payment() RETURNS trigger LANGUAGE plpgsql AS $$
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
    IF NEW.employee_party_id <> l.employee_party_id THEN
        RAISE EXCEPTION 'payment employee % does not match payroll line %', NEW.employee_party_id, l.id
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
END $$;

-- Pays salary: records the payment and its journal. Dr SALARY PAYABLE (head office) / Cr CASH (store) or BANK.
CREATE OR REPLACE FUNCTION hr.pay_salary(p_company uuid, p_line uuid, p_amount numeric, p_method text,
                                         p_paid_on date, p_user uuid, p_reference text DEFAULT NULL)
RETURNS uuid LANGUAGE plpgsql AS $$
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
    VALUES (p_company, p_paid_on, l.store_id, 'SALARY_PAYMENT', 'Salary paid to employee ' || l.employee_party_id, p_user)
    RETURNING id INTO v_entry;
    INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, party_id, debit, credit)
    VALUES (p_company, v_entry, p_paid_on, 1, '00000000-0000-0000-0000-000000000000'::uuid, v_pay, l.employee_party_id, core.round_money(p_company, p_amount), 0),
           (p_company, v_entry, p_paid_on, 2, l.store_id, v_cash, NULL, 0, core.round_money(p_company, p_amount));

    INSERT INTO hr.salary_payments (company_id, store_id, payroll_line_id, employee_party_id, paid_on, amount,
        method, reference, journal_entry_id, paid_by)
    VALUES (p_company, l.store_id, p_line, l.employee_party_id, p_paid_on, core.round_money(p_company, p_amount),
        p_method, p_reference, v_entry, p_user)
    RETURNING id INTO v_id;
    RETURN v_id;
END $$;

-- Expense approval gate: only head-office expenses take allocations, and only before approval.
CREATE OR REPLACE FUNCTION exp.check_allocation_allowed() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    e exp.expenses%ROWTYPE;
BEGIN
    SELECT * INTO e FROM exp.expenses WHERE company_id = NEW.company_id AND id = NEW.expense_id;
    IF e.store_id <> '00000000-0000-0000-0000-000000000000'::uuid THEN
        RAISE EXCEPTION 'only head-office expenses (store 0) can be allocated to stores' USING ERRCODE = 'check_violation';
    END IF;
    IF e.status NOT IN ('DRAFT','SUBMITTED') THEN
        RAISE EXCEPTION 'expense % is %, allocations can change only before approval', e.id, e.status
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

-- Submit: the recorder sends a DRAFT expense for approval.
CREATE OR REPLACE FUNCTION exp.submit_expense(p_company uuid, p_expense uuid, p_user uuid)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'EXPENSE_RECORD');
    UPDATE exp.expenses SET status = 'SUBMITTED', submitted_by = p_user, submitted_at = now()
     WHERE company_id = p_company AND id = p_expense AND status = 'DRAFT';
    IF NOT FOUND THEN
        RAISE EXCEPTION 'expense % is not a DRAFT', p_expense USING ERRCODE = 'check_violation';
    END IF;
END $$;

-- Reject: a SUBMITTED expense goes back with a reason.
CREATE OR REPLACE FUNCTION exp.reject_expense(p_company uuid, p_expense uuid, p_user uuid, p_reason text)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'EXPENSE_APPROVE');
    UPDATE exp.expenses SET status = 'REJECTED', rejected_reason = p_reason, approved_by = p_user, approved_at = now()
     WHERE company_id = p_company AND id = p_expense AND status = 'SUBMITTED';
    IF NOT FOUND THEN
        RAISE EXCEPTION 'expense % is not SUBMITTED', p_expense USING ERRCODE = 'check_violation';
    END IF;
END $$;

-- Approve and post: Dr expense category account (per allocated store) / Cr EXPENSE PAYABLE (head office).
-- The submitter cannot approve their own expense.
CREATE OR REPLACE FUNCTION exp.approve_expense(p_company uuid, p_expense uuid, p_user uuid)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    e         exp.expenses%ROWTYPE;
    v_gl      uuid;
    v_payable uuid;
    v_entry   uuid;
    v_next    int;
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'EXPENSE_APPROVE');
    SELECT * INTO e FROM exp.expenses WHERE company_id = p_company AND id = p_expense FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'expense % not found', p_expense; END IF;
    IF e.status <> 'SUBMITTED' THEN
        RAISE EXCEPTION 'expense % is %, only a SUBMITTED expense can be approved', e.id, e.status
            USING ERRCODE = 'check_violation';
    END IF;
    IF e.submitted_by = p_user THEN
        RAISE EXCEPTION 'the person who submitted expense % cannot approve it', e.id USING ERRCODE = 'check_violation';
    END IF;
    SELECT gl_account_id INTO v_gl FROM exp.expense_categories WHERE company_id = p_company AND id = e.category_id;
    SELECT id INTO v_payable FROM acct.accounts WHERE company_id = p_company AND store_id = '00000000-0000-0000-0000-000000000000'::uuid AND system_role = 'EXPENSE_PAYABLE';

    INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
    VALUES (p_company, e.expense_date, e.store_id, 'EXPENSE', e.id, 'Expense ' || e.id || ' approved', p_user)
    RETURNING id INTO v_entry;

    INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, debit, credit)
    SELECT p_company, v_entry, e.expense_date, ROW_NUMBER() OVER (ORDER BY x.store_id), x.store_id, v_gl, x.amount, 0
      FROM (
        SELECT a.store_id, a.amount FROM exp.expense_allocations a
         WHERE a.company_id = p_company AND a.expense_id = e.id
        UNION ALL
        SELECT e.store_id, e.amount
         WHERE NOT EXISTS (SELECT 1 FROM exp.expense_allocations a
                            WHERE a.company_id = p_company AND a.expense_id = e.id)
      ) x;

    SELECT COUNT(*) + 1 INTO v_next FROM acct.journal_lines
     WHERE company_id = p_company AND entry_id = v_entry AND entry_date = e.expense_date;
    INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, debit, credit)
    VALUES (p_company, v_entry, e.expense_date, v_next, '00000000-0000-0000-0000-000000000000'::uuid, v_payable, 0, e.amount);

    UPDATE exp.expenses SET status = 'POSTED', approved_by = p_user, approved_at = now(), journal_entry_id = v_entry
     WHERE company_id = p_company AND id = p_expense;
    RETURN v_entry;
END $$;

-- Transfer quantities change only through item scans, shipping and closing (they set the internal flag).
CREATE OR REPLACE FUNCTION inv.guard_transfer_lines() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF COALESCE(current_setting('app.internal_transfer_write', true), '') = 'on' THEN
        RETURN NEW;
    END IF;
    IF TG_OP = 'INSERT' THEN
        IF NEW.qty_scanned <> 0 OR NEW.qty_shipped <> 0 OR NEW.qty_received <> 0 OR NEW.unit_cost <> 0 THEN
            RAISE EXCEPTION 'transfer quantities are set only by item scans and shipping' USING ERRCODE = 'insufficient_privilege';
        END IF;
    ELSIF (NEW.qty_scanned, NEW.qty_shipped, NEW.qty_received, NEW.unit_cost)
          IS DISTINCT FROM (OLD.qty_scanned, OLD.qty_shipped, OLD.qty_received, OLD.unit_cost) THEN
        RAISE EXCEPTION 'transfer quantities change only through item scans, shipping and closing (line %)', OLD.id
            USING ERRCODE = 'insufficient_privilege';
    END IF;
    RETURN NEW;
END $$;

-- Scan gate: validates each scanned piece before it is stored.
--  SEND    : transfer DRAFT, sending store, and enough stock for everything scanned so far (incl. this piece).
--  RECEIVE : transfer SHIPPED/RECEIVING, receiving store, barcode on the transfer, not over-received.
-- Concurrent scans of one transfer are serialised by the transfer row lock.
CREATE OR REPLACE FUNCTION inv.check_transfer_scan() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    t          inv.transfers%ROWTYPE;
    v_variant  uuid;
    v_on_hand  numeric;
    v_scanned  numeric;
    v_shipped  numeric;
    v_received numeric;
BEGIN
    SELECT * INTO t FROM inv.transfers WHERE company_id = NEW.company_id AND id = NEW.transfer_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'transfer % not found', NEW.transfer_id;
    END IF;
    SELECT v.id INTO v_variant FROM catalog.variants v
     WHERE v.company_id = NEW.company_id AND v.barcode = NEW.barcode AND NOT v.is_deleted;
    IF v_variant IS NULL THEN
        RAISE EXCEPTION 'barcode % is not a known article size', NEW.barcode USING ERRCODE = 'no_data_found';
    END IF;
    NEW.variant_id := v_variant;

    IF NEW.phase = 'SEND' THEN
        IF t.status <> 'DRAFT' THEN
            RAISE EXCEPTION 'transfer % is %, items can be scanned for sending only while DRAFT', t.id, t.status
                USING ERRCODE = 'check_violation';
        END IF;
        IF NEW.store_id <> t.from_store_id THEN
            RAISE EXCEPTION 'only the sending store can scan items for transfer %', t.id USING ERRCODE = 'insufficient_privilege';
        END IF;
        SELECT l.qty_scanned INTO v_scanned FROM inv.transfer_lines l
         WHERE l.company_id = t.company_id AND l.transfer_id = t.id AND l.variant_id = v_variant;
        v_scanned := COALESCE(v_scanned, 0);
        SELECT b.qty_on_hand INTO v_on_hand FROM inv.stock_balances b
         WHERE b.company_id = t.company_id AND b.store_id = t.from_store_id AND b.variant_id = v_variant;
        IF COALESCE(v_on_hand, 0) < v_scanned + 1 THEN
            RAISE EXCEPTION 'insufficient stock at the sending store for barcode %: on hand %, already scanned %',
                NEW.barcode, COALESCE(v_on_hand, 0), v_scanned USING ERRCODE = 'check_violation';
        END IF;
    ELSE
        IF t.status NOT IN ('SHIPPED','RECEIVING') THEN
            RAISE EXCEPTION 'transfer % is %, items can be received only after shipping', t.id, t.status
                USING ERRCODE = 'check_violation';
        END IF;
        IF NEW.store_id <> t.to_store_id THEN
            RAISE EXCEPTION 'only the receiving store can receive transfer %', t.id USING ERRCODE = 'insufficient_privilege';
        END IF;
        SELECT l.qty_shipped, l.qty_received INTO v_shipped, v_received FROM inv.transfer_lines l
         WHERE l.company_id = t.company_id AND l.transfer_id = t.id AND l.variant_id = v_variant;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'barcode % was not on transfer % (unexpected item)', NEW.barcode, t.id
                USING ERRCODE = 'check_violation';
        END IF;
        IF v_received + 1 > v_shipped THEN
            RAISE EXCEPTION 'over-received: barcode % was sent %, already received %', NEW.barcode, v_shipped, v_received
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NEW;
END $$;

-- Applies an accepted scan to the transfer lines and writes the transfer event.
CREATE OR REPLACE FUNCTION inv.apply_transfer_scan() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM set_config('app.internal_transfer_write', 'on', true);
    IF NEW.phase = 'SEND' THEN
        INSERT INTO inv.transfer_lines (company_id, transfer_id, variant_id, qty_scanned)
        VALUES (NEW.company_id, NEW.transfer_id, NEW.variant_id, 1)
        ON CONFLICT (company_id, transfer_id, variant_id)
        DO UPDATE SET qty_scanned = inv.transfer_lines.qty_scanned + 1;
    ELSE
        UPDATE inv.transfer_lines SET qty_received = qty_received + 1
         WHERE company_id = NEW.company_id AND transfer_id = NEW.transfer_id AND variant_id = NEW.variant_id;
        UPDATE inv.transfers SET status = 'RECEIVING'
         WHERE company_id = NEW.company_id AND id = NEW.transfer_id AND status = 'SHIPPED';
    END IF;
    INSERT INTO inv.transfer_events (company_id, transfer_id, variant_id, event_type, qty, user_id)
    VALUES (NEW.company_id, NEW.transfer_id, NEW.variant_id, 'SCAN', 1, NEW.scanned_by);
    RETURN NULL;
END $$;

-- Receiving close: stock received goes into the receiver's stock; pieces not received become open loss cases.
-- Dr STOCK (receiver) / Cr TRANSIT (head office) for the received value. Missing pieces stay in TRANSIT until decided.
CREATE OR REPLACE FUNCTION inv.close_transfer(p_company uuid, p_transfer uuid, p_user uuid)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    t          inv.transfers%ROWTYPE;
    r          record;
    v_date     date;
    v_entry    uuid;
    v_transit  uuid;
    v_stock    uuid;
    v_value    numeric := 0;
    v_missing  boolean := false;
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'TRANSFER_RECEIVE');
    PERFORM set_config('app.internal_transfer_write', 'on', true);
    SELECT * INTO t FROM inv.transfers WHERE company_id = p_company AND id = p_transfer FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'transfer % not found', p_transfer; END IF;
    IF t.status NOT IN ('SHIPPED','RECEIVING') THEN
        RAISE EXCEPTION 'transfer % is %, only a SHIPPED or RECEIVING transfer can be closed', t.id, t.status
            USING ERRCODE = 'check_violation';
    END IF;
    SELECT (now() AT TIME ZONE c.timezone)::date INTO v_date FROM platform.companies c WHERE c.id = p_company;
    SELECT id INTO v_transit FROM acct.accounts WHERE company_id = p_company AND store_id = '00000000-0000-0000-0000-000000000000'::uuid AND system_role = 'TRANSIT';
    SELECT id INTO v_stock FROM acct.accounts WHERE company_id = p_company AND store_id = t.to_store_id AND system_role = 'STOCK';

    FOR r IN SELECT l.* FROM inv.transfer_lines l
              WHERE l.company_id = p_company AND l.transfer_id = p_transfer ORDER BY l.variant_id
    LOOP
        IF r.qty_received > 0 THEN
            PERFORM inv.post_stock_move(p_company, t.to_store_id, r.variant_id, r.qty_received,
                                        'TRANSFER_IN', t.id, r.unit_cost, v_date);
            v_value := v_value + r.qty_received * r.unit_cost;
        END IF;
        IF r.qty_shipped - r.qty_received > 0 THEN
            INSERT INTO inv.transfer_loss_cases (company_id, transfer_id, transfer_line_id, qty, reason, raised_by)
            VALUES (p_company, t.id, r.id, r.qty_shipped - r.qty_received, 'NOT_RECEIVED_AT_CLOSE', p_user);
            UPDATE inv.transfer_lines SET qty_missing = r.qty_shipped - r.qty_received
             WHERE company_id = p_company AND id = r.id;
            v_missing := true;
        END IF;
    END LOOP;

    IF v_value > 0 THEN
        INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
        VALUES (p_company, v_date, t.to_store_id, 'TRANSFER', t.id,
                'Transfer ' || t.transfer_no || ' received', p_user)
        RETURNING id INTO v_entry;
        INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, debit, credit)
        VALUES (p_company, v_entry, v_date, 1, t.to_store_id, v_stock, core.round_money(p_company, v_value), 0),
               (p_company, v_entry, v_date, 2, '00000000-0000-0000-0000-000000000000'::uuid, v_transit, 0, core.round_money(p_company, v_value));
    END IF;

    UPDATE inv.transfers
       SET status = CASE WHEN v_missing THEN 'COMPLETED_WITH_MISSING' ELSE 'COMPLETED' END,
           closed_by = p_user, closed_at = now()
     WHERE company_id = p_company AND id = p_transfer;
    INSERT INTO inv.transfer_events (company_id, transfer_id, event_type, user_id)
    VALUES (p_company, p_transfer, 'COMPLETE', p_user);
END $$;

-- Decides an open loss case. FOUND: the pieces go into the receiver's stock.
-- CHARGE: the loss is booked to the chosen bearer (COMPANY, SENDER_STORE or RECEIVER_STORE).
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

-- Claims: approval removes the pieces from stock at average cost and posts the loss.
-- Dr STOCK LOSS (store) / Cr STOCK (store).
CREATE OR REPLACE FUNCTION inv.approve_claim(p_company uuid, p_claim uuid, p_user uuid)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    c        inv.claims%ROWTYPE;
    v_date   date;
    v_cost   numeric;
    v_value  numeric;
    v_entry  uuid;
    v_loss   uuid;
    v_stock  uuid;
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'CLAIM_APPROVE');
    SELECT * INTO c FROM inv.claims WHERE company_id = p_company AND id = p_claim FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'claim % not found', p_claim; END IF;
    IF c.status <> 'REQUESTED' THEN
        RAISE EXCEPTION 'claim % is %, only a REQUESTED claim can be approved', c.id, c.status USING ERRCODE = 'check_violation';
    END IF;
    SELECT (now() AT TIME ZONE tz.timezone)::date INTO v_date FROM platform.companies tz WHERE tz.id = p_company;
    v_cost  := inv.post_stock_move(p_company, c.store_id, c.variant_id, -c.qty, 'CLAIM', c.id, NULL, v_date);
    v_value := core.round_money(p_company, c.qty * v_cost);
    SELECT id INTO v_loss  FROM acct.accounts WHERE company_id = p_company AND store_id = c.store_id AND system_role = 'STOCK_LOSS';
    SELECT id INTO v_stock FROM acct.accounts WHERE company_id = p_company AND store_id = c.store_id AND system_role = 'STOCK';
    IF v_value > 0 THEN
        INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
        VALUES (p_company, v_date, c.store_id, 'CLAIM', c.id, 'Claim ' || c.claim_no || ' approved', p_user)
        RETURNING id INTO v_entry;
        INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, debit, credit)
        VALUES (p_company, v_entry, v_date, 1, c.store_id, v_loss, v_value, 0),
               (p_company, v_entry, v_date, 2, c.store_id, v_stock, 0, v_value);
    END IF;
    UPDATE inv.claims SET status = 'APPROVED', decided_by = p_user, decided_at = now(),
                          unit_cost = v_cost, journal_entry_id = v_entry
     WHERE company_id = p_company AND id = p_claim;
    RETURN v_entry;
END $$;

CREATE OR REPLACE FUNCTION inv.reject_claim(p_company uuid, p_claim uuid, p_user uuid, p_reason text)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'CLAIM_APPROVE');
    UPDATE inv.claims SET status = 'REJECTED', decided_by = p_user, decided_at = now(), reject_reason = p_reason
     WHERE company_id = p_company AND id = p_claim AND status = 'REQUESTED';
    IF NOT FOUND THEN
        RAISE EXCEPTION 'claim % is not REQUESTED', p_claim USING ERRCODE = 'check_violation';
    END IF;
END $$;

-- Price band of a sale line from its list price (or net price, per company setting), when the app did not set one.
CREATE OR REPLACE FUNCTION catalog.assign_price_band() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_basis text;
    v_price numeric;
    v_band  uuid;
BEGIN
    IF NEW.price_band_id IS NULL THEN
        SELECT c.price_band_basis INTO v_basis FROM platform.companies c WHERE c.id = NEW.company_id;
        v_price := CASE WHEN v_basis = 'NET' THEN NEW.unit_price ELSE NEW.list_price END;
        SELECT b.id INTO v_band FROM catalog.price_bands b
         WHERE b.company_id = NEW.company_id AND NOT b.is_deleted
           AND v_price >= b.min_price AND (b.max_price IS NULL OR v_price <= b.max_price)
         LIMIT 1;
        NEW.price_band_id := v_band;
    END IF;
    RETURN NEW;
END $$;

-- Vendor-article rows may only point at a real vendor.
CREATE OR REPLACE FUNCTION purch.check_vendor_article() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM party.parties p
                    WHERE p.company_id = NEW.company_id AND p.id = NEW.vendor_party_id
                      AND p.is_vendor AND NOT p.is_deleted) THEN
        RAISE EXCEPTION 'party % is not an active vendor', NEW.vendor_party_id USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

-- =====================================================================
-- COMPANY SEED: head office store, channels, default chart of accounts, walk-in customer
-- Runs for every new company. Sets the company context so RLS allows the inserts.
-- =====================================================================
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

CREATE OR REPLACE FUNCTION platform.on_company_created() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM set_config('app.company_id', NEW.id::text, true);
    PERFORM set_config('app.all_stores', 'on', true);

    INSERT INTO core.stores (company_id, id, code, name, is_head_office)
    VALUES (NEW.id, '00000000-0000-0000-0000-000000000000'::uuid, 'HO', 'Head Office', true);

    INSERT INTO integ.channels (company_id, code, name, kind, is_active) VALUES
        (NEW.id, 'POS',     'Store POS', 'STORE_POS', true),
        (NEW.id, 'SHOPIFY', 'Shopify',   'ONLINE',    false);

    PERFORM acct.seed_default_chart(NEW.id);

    INSERT INTO party.parties (company_id, code, name, is_customer)
    VALUES (NEW.id, 'WALKIN', 'Cash Sale', true);

    RETURN NULL;
END $$;

-- Each store gets its own stock, cash, sales, cost and loss accounts.
CREATE OR REPLACE FUNCTION core.on_store_created() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO acct.accounts (company_id, code, main_type, group_code, name, store_id, system_role) VALUES
        (NEW.company_id, 'STOCK-' || NEW.code,        'STOCK',   '501', 'Stock: ' || NEW.name,          NEW.id, 'STOCK'),
        (NEW.company_id, 'CASH-' || NEW.code,         'ASSET',   '101', 'Cash in hand: ' || NEW.name,   NEW.id, 'CASH'),
        (NEW.company_id, 'SALES-' || NEW.code,        'INCOME',  '401', 'Sales: ' || NEW.name,          NEW.id, 'SALES'),
        (NEW.company_id, 'COGS-' || NEW.code,         'EXPENSE', '602', 'Cost of goods sold: ' || NEW.name, NEW.id, 'COGS'),
        (NEW.company_id, 'LOSS-' || NEW.code,         'EXPENSE', '601', 'Stock loss: ' || NEW.name,     NEW.id, 'STOCK_LOSS'),
        (NEW.company_id, 'SALARY-' || NEW.code,       'EXPENSE', '603', 'Salary expense: ' || NEW.name, NEW.id, 'SALARY_EXPENSE');
    RETURN NULL;
END $$;

-- =====================================================================
-- TRIGGERS
-- =====================================================================
-- Company and store creation
CREATE TRIGGER trg_company_created AFTER INSERT ON platform.companies
    FOR EACH ROW EXECUTE FUNCTION platform.on_company_created();
CREATE TRIGGER trg_store_created AFTER INSERT ON core.stores
    FOR EACH ROW WHEN (NEW.id <> '00000000-0000-0000-0000-000000000000'::uuid) EXECUTE FUNCTION core.on_store_created();

-- Append-only tables
CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON acct.journal_entries
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON acct.journal_lines
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON inv.stock_ledger
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON inv.transfer_events
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON catalog.price_events
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
CREATE TRIGGER trg_no_change BEFORE UPDATE OR DELETE ON audit.activity_log
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();

-- Accounting
CREATE TRIGGER trg_period_open BEFORE INSERT ON acct.journal_entries
    FOR EACH ROW EXECUTE FUNCTION acct.check_period_open();
CREATE CONSTRAINT TRIGGER trg_entry_balanced AFTER INSERT ON acct.journal_entries
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION acct.check_entry_balanced();
CREATE TRIGGER trg_apply_balances AFTER INSERT ON acct.journal_lines
    FOR EACH ROW EXECUTE FUNCTION acct.apply_line_to_balances();

-- Expense allocations and promotion scope (deferred: checked once the whole change is in)
CREATE CONSTRAINT TRIGGER trg_alloc_total AFTER INSERT OR UPDATE OR DELETE ON exp.expense_allocations
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION exp.check_allocation_total();
CREATE CONSTRAINT TRIGGER trg_rule_scope_stores AFTER INSERT OR UPDATE OR DELETE ON promo.rule_stores
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION promo.check_rule_scope();
CREATE CONSTRAINT TRIGGER trg_rule_scope_channels AFTER INSERT OR UPDATE OR DELETE ON promo.rule_channels
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION promo.check_rule_scope();
CREATE TRIGGER trg_rule_target BEFORE INSERT OR UPDATE ON promo.rule_targets
    FOR EACH ROW EXECUTE FUNCTION promo.check_rule_target();

-- Barcode uniqueness across articles and deals
CREATE TRIGGER trg_barcode_variant BEFORE INSERT OR UPDATE ON catalog.variants
    FOR EACH ROW EXECUTE FUNCTION catalog.check_barcode_free();
CREATE TRIGGER trg_barcode_bundle BEFORE INSERT OR UPDATE ON promo.bundles
    FOR EACH ROW EXECUTE FUNCTION catalog.check_barcode_free();

-- Reporting: each sale marks its store-day for the rollup worker
CREATE TRIGGER trg_mark_dirty AFTER INSERT OR UPDATE ON sales.invoices
    FOR EACH ROW EXECUTE FUNCTION rpt.mark_dirty();

-- Audit trail for money and master data changes
DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['promo.rules','catalog.variant_prices','party.parties','acct.vouchers',
                             'sec.users','hr.commission_plans','hr.payroll_runs','exp.expenses',
                             'inv.transfers','purch.invoices','sales.invoices','inv.documents',
                             'hr.commission_rules','hr.commission_plan_members','hr.salary_terms',
                             'inv.claims','purch.vendor_articles'] LOOP
        EXECUTE format('CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON %s
                        FOR EACH ROW EXECUTE FUNCTION audit.log_row_change()', t);
    END LOOP;
END $$;

-- =====================================================================
-- V5 TRIGGERS
-- =====================================================================
CREATE TRIGGER trg_guard_transfer_lines BEFORE INSERT OR UPDATE ON inv.transfer_lines
    FOR EACH ROW EXECUTE FUNCTION inv.guard_transfer_lines();
CREATE TRIGGER trg_price_band BEFORE INSERT ON sales.invoice_lines
    FOR EACH ROW EXECUTE FUNCTION catalog.assign_price_band();
CREATE TRIGGER trg_commission_line AFTER INSERT ON sales.invoice_lines
    FOR EACH ROW EXECUTE FUNCTION hr.accrue_line_commission();
CREATE TRIGGER trg_commission_void AFTER UPDATE OF status ON sales.invoices
    FOR EACH ROW WHEN (NEW.status = 'VOID' AND OLD.status IS DISTINCT FROM 'VOID')
    EXECUTE FUNCTION hr.reverse_invoice_commission();
CREATE TRIGGER trg_salary_term AFTER INSERT OR UPDATE OF base_salary, pay_frequency ON party.staff_profiles
    FOR EACH ROW EXECUTE FUNCTION hr.log_salary_term();
CREATE TRIGGER trg_salary_payment BEFORE INSERT ON hr.salary_payments
    FOR EACH ROW EXECUTE FUNCTION hr.apply_salary_payment();
CREATE TRIGGER trg_expense_alloc_allowed BEFORE INSERT ON exp.expense_allocations
    FOR EACH ROW EXECUTE FUNCTION exp.check_allocation_allowed();
CREATE TRIGGER trg_transfer_scan_check BEFORE INSERT ON inv.transfer_scans
    FOR EACH ROW EXECUTE FUNCTION inv.check_transfer_scan();
CREATE TRIGGER trg_transfer_scan_apply AFTER INSERT ON inv.transfer_scans
    FOR EACH ROW EXECUTE FUNCTION inv.apply_transfer_scan();
CREATE TRIGGER trg_vendor_article_check BEFORE INSERT OR UPDATE ON purch.vendor_articles
    FOR EACH ROW EXECUTE FUNCTION purch.check_vendor_article();
CREATE TRIGGER trg_no_change_salary_pay BEFORE UPDATE OR DELETE ON hr.salary_payments
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
CREATE TRIGGER trg_no_change_scans BEFORE UPDATE OR DELETE ON inv.transfer_scans
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();

-- =====================================================================
-- V5 CHANGE: RETURN QUANTITY LIMITS (sales and purchases)
-- =====================================================================
-- Requirement (master prompt, Part 12 and 14.2): a return may not exceed the quantity
-- of the original line, counting earlier returns of that line. Voided returns do not count.
-- Enforced here, so offline and online returns follow the same rule.

CREATE OR REPLACE FUNCTION sales.check_return_quantity() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_orig_qty   numeric;
    v_already    numeric;
BEGIN
    IF NEW.original_line_id IS NULL THEN
        RETURN NEW;   -- the table check constraint refuses a return without an original line
    END IF;
    -- lock the original sale line: two returns of the same line are serialised here
    SELECT qty INTO v_orig_qty FROM sales.invoice_lines
     WHERE company_id = NEW.company_id AND id = NEW.original_line_id
       AND sale_date = NEW.original_line_sale_date
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'original sale line % not found', NEW.original_line_id USING ERRCODE = 'foreign_key_violation';
    END IF;

    SELECT COALESCE(SUM(-r.qty), 0) INTO v_already
      FROM sales.invoice_lines r
      JOIN sales.invoices h ON h.company_id = r.company_id AND h.id = r.invoice_id
     WHERE r.company_id = NEW.company_id AND r.doc_type = 'RETURN'
       AND r.original_line_id = NEW.original_line_id AND r.original_line_sale_date = NEW.original_line_sale_date
       AND h.status <> 'VOID';

    IF v_already + (-NEW.qty) > v_orig_qty THEN
        RAISE EXCEPTION 'return of % exceeds the sold quantity: sold %, already returned %',
            -NEW.qty, v_orig_qty, v_already USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_return_qty BEFORE INSERT ON sales.invoice_lines
    FOR EACH ROW WHEN (NEW.doc_type = 'RETURN')
    EXECUTE FUNCTION sales.check_return_quantity();

CREATE OR REPLACE FUNCTION purch.check_return_quantity() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_type       text;
    v_orig_qty   numeric;
    v_already    numeric;
BEGIN
    SELECT doc_type INTO v_type FROM purch.invoices WHERE company_id = NEW.company_id AND id = NEW.invoice_id;
    IF v_type IS DISTINCT FROM 'RETURN' THEN
        RAISE EXCEPTION 'only a purchase return may point at an original purchase line' USING ERRCODE = 'check_violation';
    END IF;

    SELECT qty INTO v_orig_qty FROM purch.invoice_lines
     WHERE company_id = NEW.company_id AND id = NEW.original_line_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'original purchase line % not found', NEW.original_line_id USING ERRCODE = 'foreign_key_violation';
    END IF;

    SELECT COALESCE(SUM(-l.qty), 0) INTO v_already
      FROM purch.invoice_lines l
      JOIN purch.invoices h ON h.company_id = l.company_id AND h.id = l.invoice_id
     WHERE l.company_id = NEW.company_id AND l.original_line_id = NEW.original_line_id
       AND h.doc_type = 'RETURN' AND h.status <> 'VOID';

    IF v_already + (-NEW.qty) > v_orig_qty THEN
        RAISE EXCEPTION 'purchase return of % exceeds the purchased quantity: purchased %, already returned %',
            -NEW.qty, v_orig_qty, v_already USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_purch_return_qty BEFORE INSERT ON purch.invoice_lines
    FOR EACH ROW WHEN (NEW.original_line_id IS NOT NULL)
    EXECUTE FUNCTION purch.check_return_quantity();
-- =====================================================================
-- PARTITIONS (monthly). Extend with core.create_monthly_partitions() before a new year starts.
-- =====================================================================
SELECT core.create_monthly_partitions('sales', 'invoice_lines',      DATE '2026-01-01', DATE '2028-01-01');
SELECT core.create_monthly_partitions('inv',   'stock_ledger',       DATE '2026-01-01', DATE '2028-01-01');
SELECT core.create_monthly_partitions('acct',  'journal_lines',      DATE '2026-01-01', DATE '2028-01-01');
SELECT core.create_monthly_partitions('rpt',   'sales_variant_day',  DATE '2026-01-01', DATE '2028-01-01');

-- =====================================================================
-- ROW-LEVEL SECURITY
-- Every tenant table: rows of one company only (tenant_isolation).
-- Store-scoped tables: also restricted to the stores the user may see (store_isolation, RESTRICTIVE).
-- The application connects as role pos_app (not the owner) and sets app.company_id, app.user_id,
-- app.store_ids / app.all_stores per transaction with set_config(..., true).
-- =====================================================================
CREATE OR REPLACE FUNCTION core.enable_rls(p_table regclass, p_store_scoped boolean) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY', p_table);
    EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY', p_table);
    EXECUTE format('CREATE POLICY tenant_isolation ON %s
                    USING (company_id = (SELECT core.current_company_id()))
                    WITH CHECK (company_id = (SELECT core.current_company_id()))', p_table);
    IF p_store_scoped THEN
        EXECUTE format('CREATE POLICY store_isolation ON %s AS RESTRICTIVE
                        USING (core.store_allowed(store_id)) WITH CHECK (core.store_allowed(store_id))', p_table);
    END IF;
END $$;

DO $$
DECLARE t text;
BEGIN
    -- company-level tables
    FOREACH t IN ARRAY ARRAY[
        'core.stores','core.attachments',
        'master.departments','master.brands','master.seasons','master.genders','master.colors',
        'master.size_groups','master.product_types','master.units','master.locations','master.cities',
        'master.categories','master.sizes',
        'party.parties','party.staff_profiles','party.staff_store_history','party.party_balances',
        'sec.users','sec.user_stores','sec.user_permissions',
        'integ.channels','integ.channel_prices','integ.location_map','integ.external_ids',
        'integ.sync_outbox','integ.webhook_inbox','integ.sync_log',
        'acct.accounts','acct.periods','acct.voucher_allocations','acct.vouchers',
        'catalog.products','catalog.product_images','catalog.variants','catalog.variant_prices',
        'catalog.store_price_overrides','catalog.price_events','catalog.price_bands',
        'promo.rules','promo.rule_targets','promo.rule_stores','promo.rule_channels',
        'promo.bundles','promo.bundle_items','promo.conflict_groups','promo.conflict_candidates',
        'inv.document_lines','inv.transfer_lines','inv.transfer_events','inv.transfer_loss_cases',
        'orders.order_lines',
        'purch.invoice_lines','purch.vendor_articles','cash.count_denominations',
        'hr.commission_plans','hr.commission_plan_members','hr.commission_rules','hr.salary_terms',
        'hr.payroll_adjustments',
        'exp.expense_categories','exp.expense_allocations','ops.exceptions',
        'rpt.saved_reports','rpt.article_velocity','rpt.stock_month_snapshot',
        'audit.activity_log'
    ] LOOP
        PERFORM core.enable_rls(t::regclass, false);
    END LOOP;

    -- store-scoped tables (have a store_id column)
    FOREACH t IN ARRAY ARRAY[
        'core.devices','core.number_series',
        'acct.journal_entries','acct.journal_lines','acct.account_month_balances',
        'inv.stock_balances','inv.stock_ledger','inv.documents','integ.stock_reservations',
        'sales.invoices','sales.invoice_lines','sales.payments','sales.price_drift_records',
        'purch.invoices','purch.vendor_payment_schedule',
        'orders.orders','cash.promises','cash.cash_counts',
        'hr.payroll_runs','hr.payroll_lines','hr.commission_entries','hr.salary_payments',
        'exp.recurring_schedules','exp.expenses',
        'promo.effective_prices','inv.claims','inv.transfer_scans','rpt.promo_usage_day',
        'rpt.dirty_queue','rpt.sales_store_day','rpt.sales_variant_day'
    ] LOOP
        PERFORM core.enable_rls(t::regclass, true);
    END LOOP;
END $$;

-- Stock transfers are visible to the sending store or the receiving store.
ALTER TABLE inv.transfers ENABLE ROW LEVEL SECURITY;
ALTER TABLE inv.transfers FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON inv.transfers
    USING (company_id = (SELECT core.current_company_id()))
    WITH CHECK (company_id = (SELECT core.current_company_id()));
CREATE POLICY store_isolation ON inv.transfers AS RESTRICTIVE
    USING (core.store_allowed(from_store_id) OR core.store_allowed(to_store_id))
    WITH CHECK (core.store_allowed(from_store_id) OR core.store_allowed(to_store_id));

-- =====================================================================
-- ROLES AND GRANTS
-- =====================================================================
-- V5 ADDITIONS: OFFLINE AND SHOPIFY SALES WITH STOCK SHORTFALL
-- =====================================================================
-- An offline POS sale (device sync) or a Shopify order is always accepted.
-- Stock is posted up to what is on hand. Any shortfall is recorded as a stock
-- exception for a manager to resolve. Stock never goes negative (qty_on_hand CHECK).
-- A live POS checkout keeps the strict rule: a sale larger than stock is refused.

-- One row per sale line that went through the tolerant posting. Makes posting idempotent.
CREATE TABLE inv.sale_stock_posts (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    invoice_line_id     uuid NOT NULL,
    line_sale_date      date NOT NULL,
    store_id            uuid NOT NULL,
    variant_id          uuid NOT NULL,
    source              text NOT NULL CHECK (source IN ('OFFLINE_SYNC','SHOPIFY')),
    requested_qty       core.qty NOT NULL CHECK (requested_qty > 0),
    posted_qty          core.qty NOT NULL CHECK (posted_qty >= 0),
    short_qty           core.qty GENERATED ALWAYS AS (requested_qty - posted_qty) STORED,
    posted_at           timestamptz NOT NULL DEFAULT now(),
    posted_by           uuid,
    PRIMARY KEY (company_id, invoice_line_id, line_sale_date),
    CHECK (posted_qty <= requested_qty),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    FOREIGN KEY (company_id, invoice_line_id, line_sale_date)
        REFERENCES sales.invoice_lines(company_id, id, sale_date),
    FOREIGN KEY (company_id, store_id)  REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id)
);

-- Shortfalls the manager must resolve. At most one per sale line.
CREATE TABLE inv.stock_exceptions (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    variant_id          uuid NOT NULL,
    invoice_id          uuid NOT NULL,
    invoice_line_id     uuid NOT NULL,
    line_sale_date      date NOT NULL,
    source              text NOT NULL CHECK (source IN ('OFFLINE_SYNC','SHOPIFY')),
    short_qty           core.qty NOT NULL CHECK (short_qty > 0),
    status              text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','RESOLVED','SALE_CANCELLED')),
    resolution_note     text,
    opened_at           timestamptz NOT NULL DEFAULT now(),
    resolved_by         uuid,
    resolved_at         timestamptz,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid,
    updated_at          timestamptz,
    updated_by          uuid,
    row_ver             bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, invoice_line_id, line_sale_date),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CHECK ((status = 'OPEN') = (resolved_at IS NULL)),
    CHECK (status = 'OPEN' OR resolved_by IS NOT NULL),
    CHECK (status <> 'SALE_CANCELLED' OR btrim(coalesce(resolution_note, '')) <> ''),
    FOREIGN KEY (company_id, invoice_line_id, line_sale_date)
        REFERENCES inv.sale_stock_posts(company_id, invoice_line_id, line_sale_date),
    FOREIGN KEY (company_id, store_id)   REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, variant_id) REFERENCES catalog.variants(company_id, id),
    FOREIGN KEY (company_id, invoice_id) REFERENCES sales.invoices(company_id, id)
);
CREATE INDEX ix_stock_exc_open ON inv.stock_exceptions (company_id, store_id, opened_at) WHERE status = 'OPEN';

CREATE TRIGGER trg_touch BEFORE UPDATE ON inv.stock_exceptions
    FOR EACH ROW EXECUTE FUNCTION core.touch_row();
CREATE TRIGGER trg_no_change_ssp BEFORE UPDATE OR DELETE ON inv.sale_stock_posts
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();
CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON inv.stock_exceptions
    FOR EACH ROW EXECUTE FUNCTION audit.log_row_change();

INSERT INTO sec.permissions (code, description) VALUES
    ('STOCK_EXCEPTION_RESOLVE', 'Resolve stock shortfalls from offline and Shopify sales')
ON CONFLICT (code) DO NOTHING;

SELECT core.enable_rls('inv.sale_stock_posts'::regclass, true);
SELECT core.enable_rls('inv.stock_exceptions'::regclass, true);

-- Post the stock of one invoice. Posts what is on hand; records the rest as an exception.
-- Lines already posted are skipped, so a repeated sync does not double-post.
-- Returns the number of exceptions opened.
CREATE OR REPLACE FUNCTION inv.post_sale_stock_tolerant(p_company uuid, p_invoice uuid,
                                                        p_source text, p_user uuid)
RETURNS integer LANGUAGE plpgsql AS $$
DECLARE
    l           record;
    v_on_hand   numeric;
    v_post      numeric;
    v_opened    integer := 0;
BEGIN
    IF p_source NOT IN ('OFFLINE_SYNC','SHOPIFY') THEN
        RAISE EXCEPTION 'tolerant posting is only for OFFLINE_SYNC or SHOPIFY sales, not %', p_source
            USING ERRCODE = 'check_violation';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM sales.invoices WHERE company_id = p_company AND id = p_invoice) THEN
        RAISE EXCEPTION 'invoice % not found', p_invoice;
    END IF;

    -- ascending variant_id: same lock order as inv.post_stock_move callers (deadlock-free)
    FOR l IN
        SELECT il.id, il.sale_date, il.store_id, il.variant_id, il.qty
          FROM sales.invoice_lines il
         WHERE il.company_id = p_company AND il.invoice_id = p_invoice AND il.doc_type = 'SALE'
         ORDER BY il.variant_id, il.id
    LOOP
        CONTINUE WHEN EXISTS (SELECT 1 FROM inv.sale_stock_posts p
                               WHERE p.company_id = p_company AND p.invoice_line_id = l.id
                                 AND p.line_sale_date = l.sale_date);

        INSERT INTO inv.stock_balances (company_id, store_id, variant_id)
        VALUES (p_company, l.store_id, l.variant_id)
        ON CONFLICT (company_id, store_id, variant_id) DO NOTHING;

        SELECT qty_on_hand INTO v_on_hand FROM inv.stock_balances
         WHERE company_id = p_company AND store_id = l.store_id AND variant_id = l.variant_id
           FOR UPDATE;

        v_post := LEAST(l.qty, v_on_hand);
        IF v_post > 0 THEN
            PERFORM inv.post_stock_move(p_company, l.store_id, l.variant_id, -v_post,
                                        'SALE', p_invoice, NULL, l.sale_date);
        END IF;

        INSERT INTO inv.sale_stock_posts (company_id, invoice_line_id, line_sale_date, store_id, variant_id,
                                          source, requested_qty, posted_qty, posted_by)
        VALUES (p_company, l.id, l.sale_date, l.store_id, l.variant_id, p_source, l.qty, v_post, p_user);

        IF l.qty > v_post THEN
            INSERT INTO inv.stock_exceptions (company_id, store_id, variant_id, invoice_id, invoice_line_id,
                                              line_sale_date, source, short_qty, created_by)
            VALUES (p_company, l.store_id, l.variant_id, p_invoice, l.id, l.sale_date,
                    p_source, l.qty - v_post, p_user);
            v_opened := v_opened + 1;
        END IF;
    END LOOP;

    RETURN v_opened;
END $$;

-- Resolve an open exception.
--   STOCK_POSTED   : the goods were found or received. Posts the missing quantity now.
--                    Refused, with the stock error, while the system still shows no stock.
--   SALE_CANCELLED : the customer order is cancelled. Needs a note. Stock does not move.
--                    The sale itself must be reversed by a return document (not automated here).
CREATE OR REPLACE FUNCTION inv.resolve_stock_exception(p_company uuid, p_exception uuid,
                                                       p_resolution text, p_user uuid, p_note text)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    e inv.stock_exceptions%ROWTYPE;
BEGIN
    PERFORM sec.require_permission(p_company, p_user, 'STOCK_EXCEPTION_RESOLVE');

    SELECT * INTO e FROM inv.stock_exceptions
     WHERE company_id = p_company AND id = p_exception FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'stock exception % not found', p_exception;
    END IF;
    IF e.status <> 'OPEN' THEN
        RAISE EXCEPTION 'stock exception % is %, only OPEN can be resolved', e.id, e.status
            USING ERRCODE = 'check_violation';
    END IF;

    IF p_resolution = 'STOCK_POSTED' THEN
        PERFORM inv.post_stock_move(p_company, e.store_id, e.variant_id, -e.short_qty,
                                    'SALE', e.invoice_id, NULL, e.line_sale_date);
    ELSIF p_resolution = 'SALE_CANCELLED' THEN
        IF btrim(coalesce(p_note, '')) = '' THEN
            RAISE EXCEPTION 'a note is required when the sale is cancelled' USING ERRCODE = 'check_violation';
        END IF;
    ELSE
        RAISE EXCEPTION 'unknown resolution %', p_resolution USING ERRCODE = 'check_violation';
    END IF;

    UPDATE inv.stock_exceptions
       SET status          = CASE WHEN p_resolution = 'STOCK_POSTED' THEN 'RESOLVED' ELSE 'SALE_CANCELLED' END,
           resolution_note = p_note,
           resolved_by     = p_user,
           resolved_at     = now()
     WHERE company_id = p_company AND id = p_exception;
END $$;

-- Open shortfalls for the manager's list.
CREATE VIEW rpt.v_stock_exceptions_open WITH (security_invoker = true) AS
SELECT e.company_id, e.store_id, e.id AS exception_id, e.invoice_id, e.invoice_line_id, e.line_sale_date,
       e.variant_id, e.source, e.short_qty, e.opened_at,
       (CURRENT_DATE - e.opened_at::date) AS days_open,
       b.qty_on_hand AS store_qty_on_hand
  FROM inv.stock_exceptions e
  LEFT JOIN inv.stock_balances b
         ON b.company_id = e.company_id AND b.store_id = e.store_id AND b.variant_id = e.variant_id
 WHERE e.status = 'OPEN';

GRANT EXECUTE ON FUNCTION inv.post_sale_stock_tolerant(uuid, uuid, text, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION inv.resolve_stock_exception(uuid, uuid, text, uuid, text) TO pos_app;
GRANT SELECT ON rpt.v_stock_exceptions_open TO pos_app;
-- =====================================================================
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pos_app') THEN
        CREATE ROLE pos_app NOLOGIN NOBYPASSRLS;
    END IF;
END $$;

GRANT USAGE ON SCHEMA platform, core, sec, master, party, catalog, integ, acct, promo, inv,
                      orders, sales, purch, cash, hr, exp, ops, rpt, audit TO pos_app;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA platform, core, sec, master, party, catalog,
                      integ, acct, promo, inv, orders, sales, purch, cash, hr, exp, ops, rpt, audit TO pos_app;
GRANT DELETE ON promo.rule_targets, promo.rule_stores, promo.rule_channels, promo.bundle_items,
                exp.expense_allocations, cash.count_denominations, inv.document_lines,
                rpt.dirty_queue, integ.sync_outbox TO pos_app;
-- Append-only: insert and read only.
REVOKE UPDATE, DELETE, TRUNCATE ON acct.journal_entries, acct.journal_lines, inv.stock_ledger,
                                   inv.transfer_events, catalog.price_events, audit.activity_log,
                                   hr.salary_payments, inv.transfer_scans, inv.sale_stock_posts FROM pos_app;
REVOKE TRUNCATE ON ALL TABLES IN SCHEMA platform, core, sec, master, party, catalog, integ, acct,
                      promo, inv, orders, sales, purch, cash, hr, exp, ops, rpt, audit FROM pos_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA core, platform, sec, master, party, catalog, integ, acct,
                      promo, inv, orders, sales, purch, cash, hr, exp, ops, rpt, audit TO pos_app;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA core, sec, catalog, audit, inv, acct, promo, rpt, exp, purch, hr TO pos_app;

-- =====================================================================
-- REPORTING VIEWS (security_invoker: row-level security of the caller applies)
-- =====================================================================
CREATE VIEW acct.v_account_balance WITH (security_invoker = true) AS
SELECT b.company_id, b.store_id, b.account_id, a.code, a.name, a.main_type,
       SUM(b.debit) AS debit, SUM(b.credit) AS credit, SUM(b.debit) - SUM(b.credit) AS balance
  FROM acct.account_month_balances b
  JOIN acct.accounts a ON a.company_id = b.company_id AND a.id = b.account_id
 GROUP BY b.company_id, b.store_id, b.account_id, a.code, a.name, a.main_type;

CREATE VIEW purch.v_invoice_balance WITH (security_invoker = true) AS
SELECT i.company_id, i.id AS invoice_id, i.store_id, i.vendor_party_id, i.bill_date, i.due_date,
       i.net_amount, COALESCE(a.paid, 0) AS paid_amount,
       i.net_amount - COALESCE(a.paid, 0) AS remaining_amount
  FROM purch.invoices i
  LEFT JOIN (SELECT company_id, purchase_invoice_id, SUM(amount) AS paid
               FROM acct.voucher_allocations WHERE purchase_invoice_id IS NOT NULL
              GROUP BY company_id, purchase_invoice_id) a
         ON a.company_id = i.company_id AND a.purchase_invoice_id = i.id
 WHERE i.doc_type = 'PURCHASE' AND i.status = 'POSTED';

-- Per vendor: ledger balance, purchases, returns, open and overdue remaining, next scheduled payment.
CREATE VIEW purch.v_vendor_summary WITH (security_invoker = true) AS
SELECT p.company_id, p.id AS vendor_party_id, p.name AS vendor_name,
       -COALESCE(b.balance, 0)            AS payable_balance,
       COALESCE(s.total_purchases, 0)     AS total_purchases,
       COALESCE(s.total_returns, 0)       AS total_returns,
       COALESCE(s.open_remaining, 0)      AS open_bill_remaining,
       COALESCE(s.overdue_remaining, 0)   AS overdue_remaining,
       n.next_due_date,
       n.next_due_amount,
       p.credit_limit,
       CASE WHEN p.credit_limit IS NULL THEN NULL
            ELSE p.credit_limit - COALESCE(s.open_remaining, 0) END AS credit_available
  FROM party.parties p
  LEFT JOIN party.party_balances b ON b.company_id = p.company_id AND b.party_id = p.id
  LEFT JOIN LATERAL (
        SELECT SUM(CASE WHEN i.doc_type = 'PURCHASE' THEN i.net_amount ELSE 0 END) AS total_purchases,
               SUM(CASE WHEN i.doc_type = 'RETURN'   THEN -i.net_amount ELSE 0 END) AS total_returns,
               SUM(GREATEST(vb.remaining_amount, 0)) AS open_remaining,
               SUM(GREATEST(vb.remaining_amount, 0)) FILTER (WHERE vb.due_date < CURRENT_DATE) AS overdue_remaining
          FROM purch.invoices i
          LEFT JOIN purch.v_invoice_balance vb ON vb.company_id = i.company_id AND vb.invoice_id = i.id
         WHERE i.company_id = p.company_id AND i.vendor_party_id = p.id AND i.status = 'POSTED'
  ) s ON true
  LEFT JOIN LATERAL (
        SELECT ps.due_date AS next_due_date, SUM(ps.planned_amount - ps.paid_amount) AS next_due_amount
          FROM purch.vendor_payment_schedule ps
         WHERE ps.company_id = p.company_id AND ps.vendor_party_id = p.id
           AND ps.status IN ('OPEN','PARTIAL')
           AND ps.due_date = (SELECT MIN(x.due_date) FROM purch.vendor_payment_schedule x
                               WHERE x.company_id = ps.company_id AND x.vendor_party_id = ps.vendor_party_id
                                 AND x.status IN ('OPEN','PARTIAL'))
         GROUP BY ps.due_date
  ) n ON true
 WHERE p.is_vendor AND NOT p.is_deleted;

-- Vendor credit limit: the open bills of a vendor plus the new bill may not exceed the limit set by the admin.
-- NULL limit = no limit. Raising the limit (audited on party.parties) is the override.
CREATE OR REPLACE FUNCTION purch.check_vendor_credit() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_limit core.money;
    v_open  numeric;
BEGIN
    SELECT credit_limit INTO v_limit FROM party.parties
     WHERE company_id = NEW.company_id AND id = NEW.vendor_party_id;
    IF v_limit IS NULL THEN
        RETURN NEW;
    END IF;
    SELECT COALESCE(SUM(GREATEST(vb.remaining_amount, 0)), 0) INTO v_open
      FROM purch.invoices i
      JOIN purch.v_invoice_balance vb ON vb.company_id = i.company_id AND vb.invoice_id = i.id
     WHERE i.company_id = NEW.company_id AND i.vendor_party_id = NEW.vendor_party_id
       AND i.id IS DISTINCT FROM NEW.id;
    IF v_open + NEW.net_amount > v_limit THEN
        RAISE EXCEPTION 'vendor credit limit % exceeded: open bills %, this bill %',
            v_limit, v_open, NEW.net_amount USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_vendor_credit BEFORE INSERT OR UPDATE OF status ON purch.invoices
    FOR EACH ROW WHEN (NEW.status = 'POSTED' AND NEW.doc_type = 'PURCHASE')
    EXECUTE FUNCTION purch.check_vendor_credit();

CREATE VIEW inv.v_transfer_loss_board WITH (security_invoker = true) AS
SELECT c.company_id, c.id AS case_id, t.id AS transfer_id, t.transfer_no,
       t.from_store_id, t.to_store_id, l.variant_id, c.qty, c.reason,
       c.bearer, c.status, c.raised_at
  FROM inv.transfer_loss_cases c
  JOIN inv.transfer_lines l ON l.company_id = c.company_id AND l.id = c.transfer_line_id
  JOIN inv.transfers t      ON t.company_id = c.company_id AND t.id = c.transfer_id;

-- Expense by store and month. Head-office expenses (store 0) appear under store 0 unless allocated.
CREATE VIEW exp.v_expense_store_month WITH (security_invoker = true) AS
SELECT e.company_id, COALESCE(a.store_id, e.store_id) AS store_id, e.category_id, e.period_month,
       SUM(COALESCE(a.amount, e.amount)) AS amount
  FROM exp.expenses e
  LEFT JOIN exp.expense_allocations a ON a.company_id = e.company_id AND a.expense_id = e.id
 WHERE e.status = 'POSTED'
 GROUP BY 1, 2, 3, 4;

CREATE VIEW hr.v_commission_summary WITH (security_invoker = true) AS
SELECT e.company_id, e.store_id, e.employee_party_id,
       date_trunc('month', e.sale_date)::date AS period_month,
       COALESCE(SUM(e.base_amount)      FILTER (WHERE e.status <> 'CANCELLED'), 0) AS eligible_base,
       COALESCE(SUM(e.commission_amount) FILTER (WHERE e.status <> 'CANCELLED'), 0) AS earned,
       COALESCE(SUM(e.commission_amount) FILTER (WHERE e.status = 'PENDING'), 0)   AS pending,
       COALESCE(SUM(e.commission_amount) FILTER (WHERE e.status IN ('APPROVED','ON_HOLD')), 0) AS approved,
       COALESCE(SUM(e.commission_amount) FILTER (WHERE e.status = 'PAID'), 0)      AS paid
  FROM hr.commission_entries e
 GROUP BY e.company_id, e.store_id, e.employee_party_id, date_trunc('month', e.sale_date)::date;

CREATE VIEW hr.v_salary_outstanding WITH (security_invoker = true) AS
SELECT l.company_id, l.store_id, l.period_month, l.employee_party_id, p.name AS employee_name,
       l.base_salary, l.commission_amount, l.incentive_amount, l.deduction_amount,
       l.net_pay, l.paid_amount, l.outstanding_amount, l.status
  FROM hr.payroll_lines l
  JOIN party.parties p ON p.company_id = l.company_id AND p.id = l.employee_party_id
 WHERE l.status <> 'CANCELLED';

CREATE VIEW exp.v_expense_approval_queue WITH (security_invoker = true) AS
SELECT e.company_id, e.id AS expense_id, e.store_id, e.category_id, e.expense_date, e.amount,
       e.description, e.bill_ref, e.submitted_by, e.submitted_at
  FROM exp.expenses e
 WHERE e.status = 'SUBMITTED';

-- Vendor order lines: pending, partial, received and outstanding quantities per article.
CREATE VIEW purch.v_vendor_order_lines WITH (security_invoker = true) AS
SELECT o.company_id, o.store_id, o.party_id AS vendor_party_id, o.id AS order_id, o.order_no, o.order_date,
       l.id AS order_line_id, l.variant_id, v.product_id, l.qty_ordered,
       l.qty_posted AS qty_received,
       CASE WHEN o.status = 'CANCELLED' THEN 0 ELSE l.qty_ordered - l.qty_posted END AS qty_outstanding,
       CASE WHEN o.status = 'CANCELLED' THEN 'CANCELLED'
            WHEN l.qty_posted = 0 THEN 'PENDING'
            WHEN l.qty_posted < l.qty_ordered THEN 'PARTIAL'
            ELSE 'RECEIVED' END AS line_status
  FROM orders.orders o
  JOIN orders.order_lines l ON l.company_id = o.company_id AND l.order_id = o.id
  JOIN catalog.variants v ON v.company_id = l.company_id AND v.id = l.variant_id
 WHERE o.order_type = 'VENDOR_PO' AND NOT o.is_deleted;

-- Vendor payment reminders. Reminders only: they never change the accounting balance.
CREATE VIEW purch.v_vendor_due WITH (security_invoker = true) AS
SELECT s.company_id, s.store_id, s.vendor_party_id, p.name AS vendor_name, s.id AS schedule_id,
       s.due_date, s.planned_amount - s.paid_amount AS amount_due,
       CASE WHEN s.due_date < current_date THEN 'OVERDUE'
            WHEN s.due_date <= current_date + 7 THEN 'DUE_7_DAYS'
            ELSE 'UPCOMING' END AS due_status,
       (current_date - s.due_date) AS days_overdue
  FROM purch.vendor_payment_schedule s
  JOIN party.parties p ON p.company_id = s.company_id AND p.id = s.vendor_party_id
 WHERE s.status IN ('OPEN','PARTIAL');

-- Non-moving stock: on hand in a store, no sales in the last 90 days.
CREATE VIEW rpt.v_non_moving_stock WITH (security_invoker = true) AS
SELECT a.company_id, a.store_id, a.variant_id, a.on_hand, a.computed_at
  FROM rpt.article_velocity a
 WHERE a.window_days = 90 AND a.units_sold = 0 AND a.on_hand > 0;

-- Transfer ideas from 30-day velocity: article is slow (or stuck) in one store and sells in another.
-- Thresholds are assumptions for the owner to confirm (see the review document).
CREATE VIEW rpt.v_transfer_opportunities WITH (security_invoker = true) AS
SELECT s.company_id, s.variant_id, s.store_id AS from_store_id, r.store_id AS to_store_id,
       s.on_hand AS from_on_hand, s.days_of_cover AS from_days_cover,
       r.units_per_day AS to_units_per_day, r.days_of_cover AS to_days_cover, r.on_hand AS to_on_hand
  FROM rpt.article_velocity s
  JOIN rpt.article_velocity r ON r.company_id = s.company_id AND r.variant_id = s.variant_id
                              AND r.window_days = s.window_days AND r.store_id <> s.store_id
 WHERE s.window_days = 30
   AND s.on_hand > 1
   AND (s.units_per_day = 0 OR s.days_of_cover > 60)
   AND r.units_per_day > 0 AND (r.days_of_cover IS NULL OR r.days_of_cover < 14);

-- Price-band performance for a date range, per store. Bands with no sales are returned as NO_SALES.
CREATE FUNCTION rpt.price_band_report(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL)
RETURNS TABLE (out_store_id uuid, out_band_id uuid, out_band_name text, out_min_price core.money,
               out_max_price core.money, out_qty core.qty, out_net core.money, out_gross_profit core.money,
               out_net_rank bigint, out_status text)
LANGUAGE sql STABLE AS $$
WITH agg AS (
    SELECT s.id AS sid, b.id AS bid, b.name AS bname, b.min_price AS bmin, b.max_price AS bmax,
           COALESCE(SUM(v.qty), 0) AS q, COALESCE(SUM(v.net_amount), 0) AS n,
           COALESCE(SUM(v.net_amount - v.cost_amount), 0) AS g
      FROM core.stores s
      JOIN catalog.price_bands b ON b.company_id = s.company_id AND NOT b.is_deleted
      LEFT JOIN rpt.sales_variant_day v ON v.company_id = s.company_id AND v.store_id = s.id
           AND v.price_band_id = b.id AND v.sale_date BETWEEN p_from AND p_to
     WHERE s.company_id = p_company AND (p_store IS NULL OR s.id = p_store) AND core.store_allowed(s.id)
     GROUP BY s.id, b.id, b.name, b.min_price, b.max_price
)
SELECT sid, bid, bname, bmin, bmax, q, n, g,
       RANK() OVER (PARTITION BY sid ORDER BY n DESC),
       CASE WHEN q = 0 THEN 'NO_SALES' ELSE 'SOLD' END
  FROM agg
 ORDER BY sid, n DESC
$$;

-- Article performance for a date range, per store, with the rank of each store for the article (store comparison).
-- Articles that do not sell in a store appear with zero, so gaps are visible.
CREATE FUNCTION rpt.article_store_report(p_company uuid, p_from date, p_to date,
                                         p_store uuid DEFAULT NULL, p_category uuid DEFAULT NULL)
RETURNS TABLE (out_product_id uuid, out_store_id uuid, out_qty core.qty, out_net core.money,
               out_gross_profit core.money, out_store_rank bigint)
LANGUAGE sql STABLE AS $$
WITH agg AS (
    SELECT p.id AS pid, s.id AS sid,
           COALESCE(SUM(v.qty), 0) AS q, COALESCE(SUM(v.net_amount), 0) AS n,
           COALESCE(SUM(v.net_amount - v.cost_amount), 0) AS g
      FROM catalog.products p
      JOIN core.stores s ON s.company_id = p.company_id
      LEFT JOIN rpt.sales_variant_day v ON v.company_id = p.company_id AND v.store_id = s.id
           AND v.product_id = p.id AND v.sale_date BETWEEN p_from AND p_to
     WHERE p.company_id = p_company AND NOT p.is_deleted
       AND (p_category IS NULL OR p.category_id = p_category)
       AND (p_store IS NULL OR s.id = p_store) AND core.store_allowed(s.id)
     GROUP BY p.id, s.id
)
SELECT pid, sid, q, n, g, RANK() OVER (PARTITION BY pid ORDER BY q DESC)
  FROM agg
 ORDER BY pid, q DESC
$$;


-- =====================================================================
-- V6 PARTS
-- =====================================================================

-- =====================================================================
-- V6: LOGIN, SESSIONS, PASSWORD RESET, LOGIN HISTORY (Part 6, Part 23.7)
-- The backend verifies the argon2id password and passes the result in.
-- The database never sees a plain password. Lockout, session expiry,
-- single-use reset tokens and the login history are enforced here.
-- Tokens are random; only their SHA-256 hash is stored.
-- Policy values are technical defaults pending owner approval.
-- =====================================================================

CREATE TABLE sec.auth_policy (
    company_id            uuid PRIMARY KEY REFERENCES platform.companies(id),
    max_failed_logins     int NOT NULL DEFAULT 5  CHECK (max_failed_logins BETWEEN 3 AND 20),
    lockout_minutes       int NOT NULL DEFAULT 15 CHECK (lockout_minutes BETWEEN 1 AND 1440),
    idle_timeout_minutes  int NOT NULL DEFAULT 30 CHECK (idle_timeout_minutes BETWEEN 5 AND 480),
    session_max_hours     int NOT NULL DEFAULT 12 CHECK (session_max_hours BETWEEN 1 AND 72),
    reset_token_minutes   int NOT NULL DEFAULT 30 CHECK (reset_token_minutes BETWEEN 5 AND 120),
    reset_max_per_hour    int NOT NULL DEFAULT 3  CHECK (reset_max_per_hour BETWEEN 1 AND 10),
    updated_at            timestamptz,
    updated_by            uuid
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON sec.auth_policy FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE FUNCTION sec.create_auth_policy() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO sec.auth_policy (company_id) VALUES (NEW.id);
    RETURN NULL;
END $$;
CREATE TRIGGER trg_auth_policy AFTER INSERT ON platform.companies
    FOR EACH ROW EXECUTE FUNCTION sec.create_auth_policy();

CREATE TABLE sec.sessions (
    company_id        uuid NOT NULL REFERENCES platform.companies(id),
    id                uuid NOT NULL DEFAULT uuidv7(),
    user_id           uuid NOT NULL,
    token_hash        text NOT NULL UNIQUE,
    created_at        timestamptz NOT NULL DEFAULT now(),
    expires_at        timestamptz NOT NULL,                 -- absolute limit
    idle_expires_at   timestamptz NOT NULL,                 -- slides forward on each use
    last_seen_at      timestamptz NOT NULL DEFAULT now(),
    ip                inet,
    device_label      text,
    user_agent        text,
    revoked_at        timestamptz,
    revoke_reason     text,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, user_id) REFERENCES sec.users(company_id, id),
    CHECK ((revoked_at IS NULL) = (revoke_reason IS NULL)),
    CHECK (idle_expires_at <= expires_at)
);
CREATE INDEX ix_sessions_user ON sec.sessions (company_id, user_id) WHERE revoked_at IS NULL;

CREATE TABLE sec.login_attempts (
    company_id        uuid NOT NULL REFERENCES platform.companies(id),
    id                uuid NOT NULL DEFAULT uuidv7(),
    user_id           uuid,
    username_entered  text,
    outcome           text NOT NULL CHECK (outcome IN ('SUCCESS','WRONG_PASSWORD','UNKNOWN_USER','LOCKED',
                          'DISABLED','LOGOUT','PASSWORD_CHANGED','RESET_REQUESTED','RESET_THROTTLED',
                          'PASSWORD_RESET','ADMIN_RESET','BLOCKED','UNBLOCKED')),
    acted_by          uuid,                               -- admin actions: who did it
    ip                inet,
    device_label      text,
    user_agent        text,
    attempted_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, user_id) REFERENCES sec.users(company_id, id)
);
CREATE INDEX ix_login_attempts_user ON sec.login_attempts (company_id, user_id, attempted_at);
CREATE TRIGGER trg_no_change_attempts BEFORE UPDATE OR DELETE ON sec.login_attempts
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();

CREATE TABLE sec.password_reset_tokens (
    company_id        uuid NOT NULL REFERENCES platform.companies(id),
    id                uuid NOT NULL DEFAULT uuidv7(),
    user_id           uuid NOT NULL,
    token_hash        text NOT NULL UNIQUE,
    created_at        timestamptz NOT NULL DEFAULT now(),
    expires_at        timestamptz NOT NULL,
    used_at           timestamptz,
    requested_ip      inet,
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, user_id) REFERENCES sec.users(company_id, id),
    CHECK (used_at IS NULL OR used_at >= created_at)
);
CREATE INDEX ix_reset_user ON sec.password_reset_tokens (company_id, user_id, created_at);

INSERT INTO sec.permissions (code, description) VALUES
    ('USER_PASSWORD_RESET', 'Reset another user''s password (temporary password, forced change at next login)'),
    ('USER_BLOCK',          'Block or unblock a user account'),
    ('LOGIN_HISTORY_VIEW',  'View the login history of other users')
ON CONFLICT (code) DO NOTHING;

SELECT core.enable_rls('sec.auth_policy'::regclass, false);
SELECT core.enable_rls('sec.sessions'::regclass, false);
SELECT core.enable_rls('sec.login_attempts'::regclass, false);
SELECT core.enable_rls('sec.password_reset_tokens'::regclass, false);

-- Token helpers. Two random UUIDs give 244 bits of entropy before hashing.
CREATE FUNCTION sec.new_token() RETURNS text LANGUAGE sql VOLATILE AS $$
    SELECT encode(sha256(convert_to(gen_random_uuid()::text || gen_random_uuid()::text, 'UTF8')), 'hex')
$$;
CREATE FUNCTION sec.token_hash(p_token text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
    SELECT encode(sha256(convert_to(p_token, 'UTF8')), 'hex')
$$;

-- Login. Returns one row. outcome: OK, INVALID (generic: unknown user, wrong password, disabled),
-- LOCKED (too many failures; locked_until is set). The caller never learns whether a user exists.
CREATE FUNCTION sec.login(p_company uuid, p_username text, p_password_ok boolean,
                          p_ip inet DEFAULT NULL, p_device text DEFAULT NULL, p_user_agent text DEFAULT NULL)
RETURNS TABLE (out_outcome text, out_user_id uuid, out_session_token text, out_must_change_password boolean, out_locked_until timestamptz)
LANGUAGE plpgsql AS $$
DECLARE
    u       sec.users%ROWTYPE;
    pol     sec.auth_policy%ROWTYPE;
    v_token text;
    v_fails int;
BEGIN
    PERFORM set_config('app.company_id', p_company::text, true);   -- RLS limits every read to this company
    SELECT * INTO pol FROM sec.auth_policy WHERE company_id = p_company;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'auth policy missing for company %', p_company USING ERRCODE = 'check_violation';
    END IF;

    SELECT * INTO u FROM sec.users
     WHERE company_id = p_company AND lower(username) = lower(p_username) AND NOT is_deleted;
    IF NOT FOUND THEN
        INSERT INTO sec.login_attempts (company_id, username_entered, outcome, ip, device_label, user_agent)
        VALUES (p_company, p_username, 'UNKNOWN_USER', p_ip, p_device, p_user_agent);
        RETURN QUERY SELECT 'INVALID'::text, NULL::uuid, NULL::text, NULL::boolean, NULL::timestamptz;
        RETURN;
    END IF;

    IF u.status = 'DISABLED' THEN
        INSERT INTO sec.login_attempts (company_id, user_id, username_entered, outcome, ip, device_label, user_agent)
        VALUES (p_company, u.id, p_username, 'DISABLED', p_ip, p_device, p_user_agent);
        RETURN QUERY SELECT 'INVALID'::text, NULL::uuid, NULL::text, NULL::boolean, NULL::timestamptz;
        RETURN;
    END IF;

    IF u.locked_until IS NOT NULL AND u.locked_until > now() THEN
        INSERT INTO sec.login_attempts (company_id, user_id, username_entered, outcome, ip, device_label, user_agent)
        VALUES (p_company, u.id, p_username, 'LOCKED', p_ip, p_device, p_user_agent);
        RETURN QUERY SELECT 'LOCKED'::text, u.id, NULL::text, NULL::boolean, u.locked_until;
        RETURN;
    END IF;

    IF NOT p_password_ok THEN
        v_fails := u.failed_logins + 1;
        IF v_fails >= pol.max_failed_logins THEN
            UPDATE sec.users SET failed_logins = 0,
                   locked_until = now() + make_interval(mins => pol.lockout_minutes)
             WHERE company_id = p_company AND id = u.id;
            INSERT INTO sec.login_attempts (company_id, user_id, username_entered, outcome, ip, device_label, user_agent)
            VALUES (p_company, u.id, p_username, 'LOCKED', p_ip, p_device, p_user_agent);
            RETURN QUERY SELECT 'LOCKED'::text, u.id, NULL::text, NULL::boolean,
                                now() + make_interval(mins => pol.lockout_minutes);
            RETURN;
        END IF;
        UPDATE sec.users SET failed_logins = v_fails WHERE company_id = p_company AND id = u.id;
        INSERT INTO sec.login_attempts (company_id, user_id, username_entered, outcome, ip, device_label, user_agent)
        VALUES (p_company, u.id, p_username, 'WRONG_PASSWORD', p_ip, p_device, p_user_agent);
        RETURN QUERY SELECT 'INVALID'::text, NULL::uuid, NULL::text, NULL::boolean, NULL::timestamptz;
        RETURN;
    END IF;

    -- success
    UPDATE sec.users SET failed_logins = 0, locked_until = NULL, last_login_at = now()
     WHERE company_id = p_company AND id = u.id;
    v_token := sec.new_token();
    INSERT INTO sec.sessions (company_id, user_id, token_hash, expires_at, idle_expires_at, ip, device_label, user_agent)
    VALUES (p_company, u.id, sec.token_hash(v_token),
            now() + make_interval(hours => pol.session_max_hours),
            now() + make_interval(mins => pol.idle_timeout_minutes),
            p_ip, p_device, p_user_agent);
    INSERT INTO sec.login_attempts (company_id, user_id, username_entered, outcome, ip, device_label, user_agent)
    VALUES (p_company, u.id, p_username, 'SUCCESS', p_ip, p_device, p_user_agent);
    RETURN QUERY SELECT 'OK'::text, u.id, v_token, u.must_change_password, NULL::timestamptz;
END $$;

-- Checks a session token on each request. Slides the idle timeout forward. Returns the user id.
CREATE FUNCTION sec.validate_session(p_company uuid, p_token text) RETURNS uuid
LANGUAGE plpgsql AS $$
DECLARE
    s   sec.sessions%ROWTYPE;
    u   sec.users%ROWTYPE;
    pol sec.auth_policy%ROWTYPE;
BEGIN
    PERFORM set_config('app.company_id', p_company::text, true);
    SELECT * INTO s FROM sec.sessions
     WHERE company_id = p_company AND token_hash = sec.token_hash(p_token);
    IF NOT FOUND OR s.revoked_at IS NOT NULL OR now() >= s.expires_at OR now() >= s.idle_expires_at THEN
        RAISE EXCEPTION 'session is missing, expired or revoked' USING ERRCODE = 'invalid_authorization_specification';
    END IF;
    SELECT * INTO u FROM sec.users WHERE company_id = p_company AND id = s.user_id;
    IF u.status <> 'ACTIVE' OR u.is_deleted THEN
        RAISE EXCEPTION 'user is not active' USING ERRCODE = 'invalid_authorization_specification';
    END IF;
    SELECT * INTO pol FROM sec.auth_policy WHERE company_id = p_company;
    UPDATE sec.sessions
       SET last_seen_at = now(),
           idle_expires_at = LEAST(expires_at, now() + make_interval(mins => pol.idle_timeout_minutes))
     WHERE company_id = p_company AND id = s.id;
    RETURN s.user_id;
END $$;

CREATE FUNCTION sec.logout(p_company uuid, p_token text) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE v_user uuid;
BEGIN
    PERFORM set_config('app.company_id', p_company::text, true);
    UPDATE sec.sessions SET revoked_at = now(), revoke_reason = 'LOGOUT'
     WHERE company_id = p_company AND token_hash = sec.token_hash(p_token) AND revoked_at IS NULL
    RETURNING user_id INTO v_user;
    IF v_user IS NOT NULL THEN
        INSERT INTO sec.login_attempts (company_id, user_id, outcome) VALUES (p_company, v_user, 'LOGOUT');
    END IF;
END $$;

-- Own password change. The backend verifies the current password and passes p_current_ok.
-- All sessions of the user end, so the user signs in again with the new password.
CREATE FUNCTION sec.change_password(p_company uuid, p_user uuid, p_current_ok boolean, p_new_hash text)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    PERFORM set_config('app.company_id', p_company::text, true);
    IF NOT p_current_ok THEN
        RAISE EXCEPTION 'current password is wrong' USING ERRCODE = 'invalid_password';
    END IF;
    IF p_new_hash IS NULL OR btrim(p_new_hash) = '' THEN
        RAISE EXCEPTION 'new password hash is required' USING ERRCODE = 'check_violation';
    END IF;
    UPDATE sec.users SET password_hash = p_new_hash, password_changed_at = now(), must_change_password = false
     WHERE company_id = p_company AND id = p_user;
    UPDATE sec.sessions SET revoked_at = now(), revoke_reason = 'PASSWORD_CHANGED'
     WHERE company_id = p_company AND user_id = p_user AND revoked_at IS NULL;
    INSERT INTO sec.login_attempts (company_id, user_id, outcome) VALUES (p_company, p_user, 'PASSWORD_CHANGED');
END $$;

-- Forgotten password. Returns the token and the phone on record, for delivery by the backend.
-- Unknown or disabled users get no row, so the caller cannot tell whether a user exists.
CREATE FUNCTION sec.request_password_reset(p_company uuid, p_username text, p_ip inet DEFAULT NULL)
RETURNS TABLE (out_user_id uuid, out_reset_token text, out_delivery_phone text)
LANGUAGE plpgsql AS $$
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
        SELECT u.id, v_token, pa.phone_primary
          FROM (SELECT 1) AS one
          LEFT JOIN party.parties pa ON pa.company_id = p_company AND pa.id = u.staff_party_id;
END $$;

-- Completes a reset with the emailed or texted token. Single use. Ends all sessions.
CREATE FUNCTION sec.complete_password_reset(p_company uuid, p_token text, p_new_hash text)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE t sec.password_reset_tokens%ROWTYPE;
BEGIN
    PERFORM set_config('app.company_id', p_company::text, true);
    IF p_new_hash IS NULL OR btrim(p_new_hash) = '' THEN
        RAISE EXCEPTION 'new password hash is required' USING ERRCODE = 'check_violation';
    END IF;
    SELECT * INTO t FROM sec.password_reset_tokens
     WHERE company_id = p_company AND token_hash = sec.token_hash(p_token)
       FOR UPDATE;
    IF NOT FOUND OR t.used_at IS NOT NULL OR now() >= t.expires_at THEN
        RAISE EXCEPTION 'reset token is invalid, used or expired' USING ERRCODE = 'invalid_authorization_specification';
    END IF;
    UPDATE sec.password_reset_tokens SET used_at = now() WHERE company_id = p_company AND id = t.id;
    UPDATE sec.users SET password_hash = p_new_hash, password_changed_at = now(), must_change_password = false,
                         failed_logins = 0, locked_until = NULL
     WHERE company_id = p_company AND id = t.user_id;
    UPDATE sec.sessions SET revoked_at = now(), revoke_reason = 'PASSWORD_RESET'
     WHERE company_id = p_company AND user_id = t.user_id AND revoked_at IS NULL;
    INSERT INTO sec.login_attempts (company_id, user_id, outcome) VALUES (p_company, t.user_id, 'PASSWORD_RESET');
    RETURN t.user_id;
END $$;

-- Administrator sets a temporary password. The user must change it at next login.
CREATE FUNCTION sec.admin_reset_password(p_company uuid, p_target uuid, p_temp_hash text)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE v_admin uuid := core.current_user_id();
BEGIN
    PERFORM set_config('app.company_id', p_company::text, true);
    PERFORM sec.require_permission(p_company, v_admin, 'USER_PASSWORD_RESET');
    IF p_temp_hash IS NULL OR btrim(p_temp_hash) = '' THEN
        RAISE EXCEPTION 'temporary password hash is required' USING ERRCODE = 'check_violation';
    END IF;
    UPDATE sec.users SET password_hash = p_temp_hash, password_changed_at = now(), must_change_password = true,
                         failed_logins = 0, locked_until = NULL
     WHERE company_id = p_company AND id = p_target AND NOT is_deleted;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'user % not found', p_target USING ERRCODE = 'no_data_found';
    END IF;
    UPDATE sec.sessions SET revoked_at = now(), revoke_reason = 'ADMIN_RESET'
     WHERE company_id = p_company AND user_id = p_target AND revoked_at IS NULL;
    INSERT INTO sec.login_attempts (company_id, user_id, outcome, acted_by) VALUES (p_company, p_target, 'ADMIN_RESET', v_admin);
END $$;

-- Blocks or unblocks an account. Blocking ends its sessions at once.
CREATE FUNCTION sec.set_user_status(p_company uuid, p_target uuid, p_status text)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE v_admin uuid := core.current_user_id();
BEGIN
    PERFORM set_config('app.company_id', p_company::text, true);
    PERFORM sec.require_permission(p_company, v_admin, 'USER_BLOCK');
    IF p_status NOT IN ('ACTIVE','DISABLED') THEN
        RAISE EXCEPTION 'status must be ACTIVE or DISABLED' USING ERRCODE = 'check_violation';
    END IF;
    UPDATE sec.users SET status = p_status WHERE company_id = p_company AND id = p_target AND NOT is_deleted;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'user % not found', p_target USING ERRCODE = 'no_data_found';
    END IF;
    IF p_status = 'DISABLED' THEN
        UPDATE sec.sessions SET revoked_at = now(), revoke_reason = 'BLOCKED'
         WHERE company_id = p_company AND user_id = p_target AND revoked_at IS NULL;
    END IF;
    INSERT INTO sec.login_attempts (company_id, user_id, outcome, acted_by)
    VALUES (p_company, p_target, CASE WHEN p_status = 'DISABLED' THEN 'BLOCKED' ELSE 'UNBLOCKED' END, v_admin);
END $$;

-- Login history. A user may always see their own. Others need LOGIN_HISTORY_VIEW.
CREATE FUNCTION sec.login_history(p_company uuid, p_user uuid DEFAULT NULL,
                                  p_from timestamptz DEFAULT now() - interval '30 days',
                                  p_to timestamptz DEFAULT now())
RETURNS TABLE (out_attempt_id uuid, out_user_id uuid, out_username text, out_outcome text,
               out_attempted_at timestamptz, out_ip inet, out_device text, out_acted_by uuid)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    IF p_user IS DISTINCT FROM core.current_user_id() THEN
        PERFORM sec.require_permission(p_company, core.current_user_id(), 'LOGIN_HISTORY_VIEW');
    END IF;
    RETURN QUERY
        SELECT a.id, a.user_id, COALESCE(a.username_entered, u.username), a.outcome,
               a.attempted_at, a.ip, a.device_label, a.acted_by
          FROM sec.login_attempts a
          LEFT JOIN sec.users u ON u.company_id = a.company_id AND u.id = a.user_id
         WHERE a.company_id = p_company
           AND (p_user IS NULL OR a.user_id = p_user)
           AND a.attempted_at BETWEEN p_from AND p_to
         ORDER BY a.attempted_at DESC, a.id DESC;
END $$;

GRANT SELECT, INSERT, UPDATE ON sec.sessions, sec.login_attempts, sec.password_reset_tokens, sec.auth_policy TO pos_app;
GRANT SELECT, INSERT, UPDATE ON sec.users TO pos_app;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA sec TO pos_app;
GRANT EXECUTE ON FUNCTION sec.login(uuid, text, boolean, inet, text, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sec.validate_session(uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sec.logout(uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sec.change_password(uuid, uuid, boolean, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sec.request_password_reset(uuid, text, inet) TO pos_app;
GRANT EXECUTE ON FUNCTION sec.complete_password_reset(uuid, text, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sec.admin_reset_password(uuid, uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sec.set_user_status(uuid, uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sec.login_history(uuid, uuid, timestamptz, timestamptz) TO pos_app;

-- =====================================================================
-- V6: SALES AND PURCHASE POSTING IN THE DATABASE (Part 9, Part 15)
-- A sale, return, purchase or purchase return posts its journal entry
-- in the same transaction that saves it. A posting that fails (closed
-- period, missing account, header not matching its lines) rejects the
-- whole document. A void posts a reversal entry, because journal
-- entries are append-only.
--
-- Accounts are chosen by system role on the head-office chart (store 0).
-- The treatment below is a DEFAULT pending the accountant's approval
-- (matrix decision 3). Change the account mapping, not the code.
--
-- Treatment (default):
--   SALE      Dr CASH (paid less change), Dr PARTY_CONTROL (customer, unpaid part),
--             Cr SALES (net less tax), Cr TAX_PAYABLE (tax),
--             Dr COGS / Cr STOCK (cost of goods sold)
--   RETURN    Dr SALES (net less tax), Dr TAX_PAYABLE, Cr PARTY_CONTROL (customer)
--             or Cr SUSPENSE (no customer; accountant clears it), Dr STOCK / Cr COGS
--             A cash refund is not modelled yet, so a return with paid or change amounts is refused.
--   PURCHASE  Dr STOCK (net payable), Cr PARTY_CONTROL (vendor)
--   PURCHASE RETURN  Dr PARTY_CONTROL (vendor), Cr STOCK
--             Discount and tax on purchases are included in stock cost (default; to be confirmed).
-- =====================================================================

CREATE FUNCTION acct.role_account(p_company uuid, p_role text) RETURNS uuid
LANGUAGE plpgsql STABLE AS $$
DECLARE v_id uuid;
BEGIN
    SELECT id INTO v_id FROM acct.accounts
     WHERE company_id = p_company AND system_role = p_role AND store_id = '00000000-0000-0000-0000-000000000000'::uuid AND is_active;
    IF v_id IS NULL THEN
        RAISE EXCEPTION 'account role % is not configured for company %', p_role, p_company
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN v_id;
END $$;

-- Adds one line. Zero amounts are skipped. Returns the next line number.
CREATE FUNCTION acct.add_line(p_company uuid, p_entry uuid, p_date date, p_line_no int, p_store uuid,
                              p_account uuid, p_party uuid, p_debit core.money, p_credit core.money,
                              p_desc text)
RETURNS int LANGUAGE plpgsql AS $$
BEGIN
    IF COALESCE(p_debit, 0) = 0 AND COALESCE(p_credit, 0) = 0 THEN
        RETURN p_line_no;
    END IF;
    INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, party_id,
                                    debit, credit, description)
    VALUES (p_company, p_entry, p_date, p_line_no, p_store, p_account, p_party,
            COALESCE(p_debit, 0), COALESCE(p_credit, 0), p_desc);
    RETURN p_line_no + 1;
END $$;

-- Reverses a posted entry: same lines, debit and credit swapped, dated today.
CREATE FUNCTION acct.reverse_entry(p_company uuid, p_entry uuid, p_source_type text, p_source_id uuid,
                                   p_desc text)
RETURNS uuid LANGUAGE plpgsql AS $$
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
    INSERT INTO acct.journal_lines (company_id, entry_id, entry_date, line_no, store_id, account_id, party_id,
                                    debit, credit, description)
    SELECT company_id, v_new, CURRENT_DATE, line_no, store_id, account_id, party_id, credit, debit,
           'Reversal: ' || COALESCE(description, '')
      FROM acct.journal_lines
     WHERE company_id = p_company AND entry_id = e.id AND entry_date = e.entry_date;
    RETURN v_new;
END $$;

-- ---------------------------------------------------------------------
-- Sales
-- ---------------------------------------------------------------------
CREATE FUNCTION sales.post_invoice_journal() RETURNS trigger LANGUAGE plpgsql AS $$
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

CREATE FUNCTION sales.post_void_journal() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.journal_entry_id IS NULL THEN
        RETURN NULL;   -- never posted (voided before commit): nothing to reverse
    END IF;
    PERFORM acct.reverse_entry(NEW.company_id, NEW.journal_entry_id,
        CASE WHEN NEW.doc_type = 'SALE' THEN 'SALE_VOID' ELSE 'SALE_RETURN_VOID' END,
        NEW.id, 'Void of invoice ' || NEW.invoice_no);
    RETURN NULL;
END $$;

-- Deferred: runs at COMMIT, after the lines are inserted, in the same transaction.
CREATE CONSTRAINT TRIGGER trg_sale_post AFTER INSERT ON sales.invoices
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sales.post_invoice_journal();
CREATE TRIGGER trg_sale_void AFTER UPDATE OF status ON sales.invoices
    FOR EACH ROW WHEN (OLD.status = 'POSTED' AND NEW.status = 'VOID') EXECUTE FUNCTION sales.post_void_journal();

-- ---------------------------------------------------------------------
-- Purchases
-- ---------------------------------------------------------------------
CREATE FUNCTION purch.post_invoice_journal() RETURNS trigger LANGUAGE plpgsql AS $$
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
        v_no := acct.add_line(h.company_id, v_entry, h.bill_date, v_no, h.store_id, acct.role_account(h.company_id, 'STOCK'), NULL, v_amt, 0, 'Stock received');
        v_no := acct.add_line(h.company_id, v_entry, h.bill_date, v_no, h.store_id, acct.role_account(h.company_id, 'PARTY_CONTROL'), h.vendor_party_id, 0, v_amt, 'Vendor payable');
    ELSIF h.doc_type = 'RETURN' THEN
        v_amt := -h.net_amount;
        INSERT INTO acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by)
        VALUES (h.company_id, h.bill_date, h.store_id, 'PURCHASE_RETURN', h.id, 'Purchase return ' || h.invoice_no, h.created_by)
        RETURNING id INTO v_entry;
        v_no := acct.add_line(h.company_id, v_entry, h.bill_date, v_no, h.store_id, acct.role_account(h.company_id, 'PARTY_CONTROL'), h.vendor_party_id, v_amt, 0, 'Vendor debited');
        v_no := acct.add_line(h.company_id, v_entry, h.bill_date, v_no, h.store_id, acct.role_account(h.company_id, 'STOCK'), NULL, 0, v_amt, 'Stock returned to vendor');
    END IF;

    UPDATE purch.invoices SET journal_entry_id = v_entry WHERE company_id = h.company_id AND id = h.id;
    RETURN NULL;
END $$;

CREATE FUNCTION purch.post_void_journal() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.journal_entry_id IS NULL THEN
        RETURN NULL;
    END IF;
    PERFORM acct.reverse_entry(NEW.company_id, NEW.journal_entry_id,
        CASE WHEN NEW.doc_type = 'PURCHASE' THEN 'PURCHASE_VOID' ELSE 'PURCHASE_RETURN_VOID' END,
        NEW.id, 'Void of purchase ' || NEW.invoice_no);
    RETURN NULL;
END $$;

CREATE CONSTRAINT TRIGGER trg_purch_post AFTER INSERT ON purch.invoices
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION purch.post_invoice_journal();
CREATE TRIGGER trg_purch_void AFTER UPDATE OF status ON purch.invoices
    FOR EACH ROW WHEN (OLD.status = 'POSTED' AND NEW.status = 'VOID') EXECUTE FUNCTION purch.post_void_journal();

GRANT EXECUTE ON FUNCTION acct.role_account(uuid, text) TO pos_app;

-- =====================================================================
-- V6: REPORTS (Part 22). Every function checks REPORT_VIEW or REPORT_ACCOUNTS
-- for the calling user (core.current_user_id). Row-level security applies to
-- every read, so a user sees only the stores they may see.
-- Totals come from aggregates taken separately, so joins do not multiply rows.
-- =====================================================================

INSERT INTO sec.permissions (code, description) VALUES
    ('REPORT_VIEW',     'Operational reports: sales, purchases, stock, vendors, commissions, expenses'),
    ('REPORT_ACCOUNTS', 'Accounting reports: ledger, trial balance, journal')
ON CONFLICT (code) DO NOTHING;

-- 1. Account ledger: every line of one account with a running balance (opening balance included).
CREATE FUNCTION rpt.account_ledger(p_company uuid, p_account uuid, p_from date, p_to date,
                                   p_store uuid DEFAULT NULL)
RETURNS TABLE (out_entry_date date, out_entry_id uuid, out_source_type text, out_description text,
               out_party_id uuid, out_debit core.money, out_credit core.money, out_running core.money)
LANGUAGE plpgsql STABLE AS $$
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
               COALESCE(l.description, e.description) AS descr, l.party_id, l.debit, l.credit
          FROM acct.journal_lines l
          JOIN acct.journal_entries e ON e.company_id = l.company_id AND e.id = l.entry_id AND e.entry_date = l.entry_date
         WHERE l.company_id = p_company AND l.account_id = p_account
           AND l.entry_date BETWEEN p_from AND p_to
           AND (p_store IS NULL OR l.store_id = p_store)
    )
    SELECT m.entry_date, m.entry_id, m.source_type, m.descr, m.party_id,
           m.debit::core.money, m.credit::core.money,
           ((SELECT bal FROM opening) + SUM(m.debit - m.credit) OVER (ORDER BY m.entry_date, m.entry_id, m.line_no))::core.money
      FROM moves m
     ORDER BY m.entry_date, m.entry_id, m.line_no;
END $$;

-- 2. Trial balance (also the detailed trial balance: opening, period movement, closing per account).
CREATE FUNCTION rpt.trial_balance(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL)
RETURNS TABLE (out_account_id uuid, out_code text, out_name text, out_main_type text,
               out_opening core.money, out_period_debit core.money, out_period_credit core.money,
               out_closing core.money)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_ACCOUNTS');
    RETURN QUERY
    SELECT a.id, a.code, a.name, a.main_type,
           COALESCE(o.bal, 0)::core.money,
           COALESCE(m.dr, 0)::core.money,
           COALESCE(m.cr, 0)::core.money,
           (COALESCE(o.bal, 0) + COALESCE(m.dr, 0) - COALESCE(m.cr, 0))::core.money
      FROM acct.accounts a
      LEFT JOIN (SELECT l.account_id, SUM(l.debit - l.credit) AS bal
                   FROM acct.journal_lines l
                  WHERE l.company_id = p_company AND l.entry_date < p_from
                    AND (p_store IS NULL OR l.store_id = p_store)
                  GROUP BY l.account_id) o ON o.account_id = a.id
      LEFT JOIN (SELECT l.account_id, SUM(l.debit) AS dr, SUM(l.credit) AS cr
                   FROM acct.journal_lines l
                  WHERE l.company_id = p_company AND l.entry_date BETWEEN p_from AND p_to
                    AND (p_store IS NULL OR l.store_id = p_store)
                  GROUP BY l.account_id) m ON m.account_id = a.id
     WHERE a.company_id = p_company
     ORDER BY a.code;
END $$;

-- 3. Account balance as of a date (net of debit and credit).
CREATE FUNCTION rpt.account_balance(p_company uuid, p_as_of date, p_store uuid DEFAULT NULL)
RETURNS TABLE (out_account_id uuid, out_code text, out_name text, out_balance core.money)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_ACCOUNTS');
    RETURN QUERY
    SELECT a.id, a.code, a.name, COALESCE(SUM(l.debit - l.credit), 0)::core.money
      FROM acct.accounts a
      LEFT JOIN acct.journal_lines l ON l.company_id = a.company_id AND l.account_id = a.id
           AND l.entry_date <= p_as_of AND (p_store IS NULL OR l.store_id = p_store)
     WHERE a.company_id = p_company
     GROUP BY a.id, a.code, a.name
     ORDER BY a.code;
END $$;

-- 4. General journal: every entry with its lines, in posting order.
CREATE FUNCTION rpt.general_journal(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL)
RETURNS TABLE (out_entry_id uuid, out_entry_date date, out_source_type text, out_source_id uuid,
               out_line_no int, out_account_code text, out_account_name text, out_party_name text,
               out_debit core.money, out_credit core.money, out_description text)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_ACCOUNTS');
    RETURN QUERY
    SELECT e.id, e.entry_date, e.source_type, e.source_id, l.line_no, a.code, a.name, p.name,
           l.debit, l.credit, COALESCE(l.description, e.description)
      FROM acct.journal_entries e
      JOIN acct.journal_lines l ON l.company_id = e.company_id AND l.entry_id = e.id AND l.entry_date = e.entry_date
      JOIN acct.accounts a ON a.company_id = l.company_id AND a.id = l.account_id
      LEFT JOIN party.parties p ON p.company_id = l.company_id AND p.id = l.party_id
     WHERE e.company_id = p_company AND e.entry_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR e.store_id = p_store)
     ORDER BY e.entry_date, e.id, l.line_no;
END $$;

-- 5. Sale register: one row per invoice (sales and returns). VOID invoices are listed with their status.
CREATE FUNCTION rpt.sale_register(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL,
                                  p_doc_type text DEFAULT NULL, p_customer uuid DEFAULT NULL)
RETURNS TABLE (out_invoice_id uuid, out_invoice_no bigint, out_doc_type text, out_sale_date date,
               out_store_id uuid, out_customer text, out_qty core.qty, out_gross core.money,
               out_discount core.money, out_net core.money, out_tax core.money, out_cost core.money,
               out_profit core.money, out_status text)
LANGUAGE plpgsql STABLE AS $$
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
      LEFT JOIN party.parties pa ON pa.company_id = h.company_id AND pa.id = h.customer_party_id
      LEFT JOIN (SELECT l.invoice_id, SUM(l.qty) AS qty
                   FROM sales.invoice_lines l
                  WHERE l.company_id = p_company AND l.sale_date BETWEEN p_from AND p_to
                  GROUP BY l.invoice_id) q ON q.invoice_id = h.id
     WHERE h.company_id = p_company AND h.sale_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR h.store_id = p_store)
       AND (p_doc_type IS NULL OR h.doc_type = p_doc_type)
       AND (p_customer IS NULL OR h.customer_party_id = p_customer)
     ORDER BY h.sale_date, h.invoice_no;
END $$;

-- 6. Sales by category. Posted invoices only. Returns are negative and reduce the totals.
CREATE FUNCTION rpt.sale_by_category(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL)
RETURNS TABLE (out_category_id uuid, out_category text, out_qty core.qty, out_net core.money,
               out_cost core.money, out_profit core.money)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT l.category_id, c.name, SUM(l.qty)::core.qty, SUM(l.net_amount)::core.money,
           SUM(l.cost_amount)::core.money, (SUM(l.net_amount) - SUM(l.cost_amount))::core.money
      FROM sales.invoice_lines l
      JOIN sales.invoices h ON h.company_id = l.company_id AND h.id = l.invoice_id AND h.status = 'POSTED'
      LEFT JOIN master.categories c ON c.company_id = l.company_id AND c.id = l.category_id
     WHERE l.company_id = p_company AND l.sale_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR l.store_id = p_store)
     GROUP BY l.category_id, c.name
     ORDER BY SUM(l.net_amount) DESC;
END $$;

-- 7. Profit by store. Posted invoices only. Gross profit = net sales less cost. Margin in percent.
CREATE FUNCTION rpt.profit_summary(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL)
RETURNS TABLE (out_store_id uuid, out_sales_net core.money, out_return_net core.money,
               out_net_sales core.money, out_cost core.money, out_gross_profit core.money,
               out_margin_pct numeric)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT h.store_id,
           COALESCE(SUM(h.net_amount) FILTER (WHERE h.doc_type = 'SALE'), 0)::core.money,
           COALESCE(SUM(h.net_amount) FILTER (WHERE h.doc_type = 'RETURN'), 0)::core.money,
           SUM(h.net_amount)::core.money,
           SUM(h.cost_amount)::core.money,
           (SUM(h.net_amount) - SUM(h.cost_amount))::core.money,
           ROUND(100.0 * (SUM(h.net_amount) - SUM(h.cost_amount)) / NULLIF(SUM(h.net_amount), 0), 2)
      FROM sales.invoices h
     WHERE h.company_id = p_company AND h.status = 'POSTED'
       AND h.sale_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR h.store_id = p_store)
     GROUP BY h.store_id
     ORDER BY h.store_id;
END $$;

-- 8. Purchase register: one row per bill or return.
CREATE FUNCTION rpt.purchase_register(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL,
                                      p_vendor uuid DEFAULT NULL)
RETURNS TABLE (out_invoice_id uuid, out_invoice_no bigint, out_doc_type text, out_bill_date date,
               out_store_id uuid, out_vendor text, out_vendor_bill_no text, out_gross core.money,
               out_discount core.money, out_tax core.money, out_net core.money, out_status text)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT i.id, i.invoice_no, i.doc_type, i.bill_date, i.store_id, p.name, i.vendor_bill_no,
           i.gross_amount, i.discount_amount, i.tax_amount, i.net_amount, i.status
      FROM purch.invoices i
      JOIN party.parties p ON p.company_id = i.company_id AND p.id = i.vendor_party_id
     WHERE i.company_id = p_company AND i.bill_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR i.store_id = p_store)
       AND (p_vendor IS NULL OR i.vendor_party_id = p_vendor)
     ORDER BY i.bill_date, i.invoice_no;
END $$;

-- 9. Vendor aging: open bills by days past due, as of a date. Payments are taken from the
--    current allocation view, so a payment made after the as-of date is already counted.
CREATE FUNCTION rpt.vendor_aging(p_company uuid, p_as_of date)
RETURNS TABLE (out_vendor_id uuid, out_vendor text, out_not_due core.money, out_days_1_30 core.money,
               out_days_31_60 core.money, out_days_61_90 core.money, out_over_90 core.money,
               out_total core.money)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT v.vendor_party_id, p.name,
           COALESCE(SUM(v.remaining_amount) FILTER (WHERE v.due_date IS NULL OR v.due_date >= p_as_of), 0)::core.money,
           COALESCE(SUM(v.remaining_amount) FILTER (WHERE p_as_of - v.due_date BETWEEN 1 AND 30), 0)::core.money,
           COALESCE(SUM(v.remaining_amount) FILTER (WHERE p_as_of - v.due_date BETWEEN 31 AND 60), 0)::core.money,
           COALESCE(SUM(v.remaining_amount) FILTER (WHERE p_as_of - v.due_date BETWEEN 61 AND 90), 0)::core.money,
           COALESCE(SUM(v.remaining_amount) FILTER (WHERE p_as_of - v.due_date > 90), 0)::core.money,
           SUM(v.remaining_amount)::core.money
      FROM purch.v_invoice_balance v
      JOIN party.parties p ON p.company_id = v.company_id AND p.id = v.vendor_party_id
     WHERE v.company_id = p_company AND v.bill_date <= p_as_of AND v.remaining_amount <> 0
     GROUP BY v.vendor_party_id, p.name
     ORDER BY p.name;
END $$;

-- 10. Stock value at current balances (qty on hand times average cost).
CREATE FUNCTION rpt.stock_value(p_company uuid, p_store uuid DEFAULT NULL, p_category uuid DEFAULT NULL)
RETURNS TABLE (out_store_id uuid, out_variant_id uuid, out_product_code text, out_qty core.qty,
               out_avg_cost core.unit_cost, out_value core.money)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT b.store_id, b.variant_id, pr.code, b.qty_on_hand, b.avg_cost,
           core.round_money(p_company, b.qty_on_hand * b.avg_cost)
      FROM inv.stock_balances b
      JOIN catalog.variants v ON v.company_id = b.company_id AND v.id = b.variant_id
      JOIN catalog.products pr ON pr.company_id = v.company_id AND pr.id = v.product_id
     WHERE b.company_id = p_company AND b.qty_on_hand <> 0
       AND (p_store IS NULL OR b.store_id = p_store)
       AND (p_category IS NULL OR pr.category_id = p_category)
     ORDER BY b.store_id, pr.code;
END $$;

-- 11. Stock adjustment and audit movements from the stock ledger.
CREATE FUNCTION rpt.stock_adjustments(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL)
RETURNS TABLE (out_txn_date date, out_store_id uuid, out_variant_id uuid, out_doc_type text,
               out_doc_id uuid, out_qty core.qty, out_unit_cost core.unit_cost, out_value core.money)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT s.txn_date, s.store_id, s.variant_id, s.doc_type, s.doc_id, s.qty, s.unit_cost,
           core.round_money(p_company, s.qty * s.unit_cost)
      FROM inv.stock_ledger s
     WHERE s.company_id = p_company AND s.doc_type IN ('ADJUSTMENT','AUDIT')
       AND s.txn_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR s.store_id = p_store)
     ORDER BY s.txn_date, s.id;
END $$;

-- 12. Commission accrual and payment by employee and status.
CREATE FUNCTION rpt.commission_accrual(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL)
RETURNS TABLE (out_employee_id uuid, out_employee text, out_status text, out_earned core.money,
               out_reversed core.money, out_net core.money, out_entries bigint)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT c.employee_party_id, p.name, c.status,
           COALESCE(SUM(c.commission_amount) FILTER (WHERE c.entry_type = 'EARN'), 0)::core.money,
           COALESCE(SUM(c.commission_amount) FILTER (WHERE c.entry_type = 'REVERSAL'), 0)::core.money,
           SUM(c.commission_amount)::core.money,
           COUNT(*)
      FROM hr.commission_entries c
      JOIN party.parties p ON p.company_id = c.company_id AND p.id = c.employee_party_id
     WHERE c.company_id = p_company AND c.sale_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR c.store_id = p_store)
     GROUP BY c.employee_party_id, p.name, c.status
     ORDER BY p.name, c.status;
END $$;

-- 13. Store expenses by store and category (posted expenses only).
CREATE FUNCTION rpt.store_expenses(p_company uuid, p_from date, p_to date, p_store uuid DEFAULT NULL)
RETURNS TABLE (out_store_id uuid, out_category_id uuid, out_category text, out_amount core.money, out_count bigint)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT e.store_id, e.category_id, c.name, SUM(e.amount)::core.money, COUNT(*)
      FROM exp.expenses e
      JOIN exp.expense_categories c ON c.company_id = e.company_id AND c.id = e.category_id
     WHERE e.company_id = p_company AND e.status = 'POSTED'
       AND e.expense_date BETWEEN p_from AND p_to
       AND (p_store IS NULL OR e.store_id = p_store)
     GROUP BY e.store_id, e.category_id, c.name
     ORDER BY e.store_id, c.name;
END $$;

-- 14. Customer balances (receivable). Ageing needs open-item tracking, which is not built yet.
CREATE FUNCTION rpt.customer_balances(p_company uuid)
RETURNS TABLE (out_customer_id uuid, out_customer text, out_balance core.money, out_last_entry date)
LANGUAGE plpgsql STABLE AS $$
BEGIN
    PERFORM sec.require_permission(p_company, core.current_user_id(), 'REPORT_VIEW');
    RETURN QUERY
    SELECT b.party_id, p.name, b.balance::core.money, b.last_entry_date
      FROM party.party_balances b
      JOIN party.parties p ON p.company_id = b.company_id AND p.id = b.party_id
     WHERE b.company_id = p_company AND p.is_customer AND b.balance <> 0
     ORDER BY p.name;
END $$;

GRANT EXECUTE ON FUNCTION rpt.account_ledger(uuid, uuid, date, date, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.trial_balance(uuid, date, date, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.account_balance(uuid, date, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.general_journal(uuid, date, date, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.sale_register(uuid, date, date, uuid, text, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.sale_by_category(uuid, date, date, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.profit_summary(uuid, date, date, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.purchase_register(uuid, date, date, uuid, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.vendor_aging(uuid, date) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.stock_value(uuid, uuid, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.stock_adjustments(uuid, date, date, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.commission_accrual(uuid, date, date, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.store_expenses(uuid, date, date, uuid) TO pos_app;
GRANT EXECUTE ON FUNCTION rpt.customer_balances(uuid) TO pos_app;

-- =====================================================================
-- V6: OFFLINE SYNC, SERVER SIDE (Part 23.3 to 23.9)
-- The browser keeps its queue in IndexedDB. The server keeps what it
-- accepted, what it refused and what conflicted, because a browser
-- can lose its storage. Idempotency is by client transaction UUID.
-- Each transaction is posted in its own database transaction. A
-- refused transaction is rolled back, and the backend then records
-- the refusal (item and conflict) in a new transaction.
-- =====================================================================

CREATE SCHEMA sync;
GRANT USAGE ON SCHEMA sync TO pos_app;

INSERT INTO sec.permissions (code, description) VALUES
    ('SYNC_CONFLICT_RESOLVE', 'Decide offline sync conflicts (accept by hand or reject, with a note)')
ON CONFLICT (code) DO NOTHING;

-- One upload from one device. Repeating the same batch UUID returns the original batch.
CREATE TABLE sync.batches (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    id              uuid NOT NULL DEFAULT uuidv7(),
    store_id        uuid NOT NULL,
    device_id       uuid NOT NULL,
    batch_uuid      uuid NOT NULL,
    item_count      int NOT NULL CHECK (item_count > 0),
    status          text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','COMPLETED','INCOMPLETE')),
    received_at     timestamptz NOT NULL DEFAULT now(),
    completed_at    timestamptz,
    accepted_count  int NOT NULL DEFAULT 0,
    conflict_count  int NOT NULL DEFAULT 0,
    rejected_count  int NOT NULL DEFAULT 0,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, device_id, batch_uuid),
    FOREIGN KEY (company_id, store_id)  REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, device_id) REFERENCES core.devices(company_id, id),
    CHECK ((status = 'OPEN') = (completed_at IS NULL)),
    CHECK (accepted_count + conflict_count + rejected_count <= item_count)
);
CREATE INDEX ix_sync_batches_device ON sync.batches (company_id, device_id, received_at);

-- Result of each transaction. A client transaction is recorded once, in any batch.
CREATE TABLE sync.batch_items (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    batch_id            uuid NOT NULL,
    item_no             int NOT NULL CHECK (item_no > 0),
    client_txn_uuid     uuid NOT NULL,
    txn_type            text NOT NULL CHECK (txn_type IN ('SALE','RETURN','PURCHASE','PURCHASE_RETURN',
                            'STOCK_ADJUSTMENT','EXPENSE','CUSTOMER_CREATE','STOCK_EXCEPTION','OTHER')),
    result              text NOT NULL CHECK (result IN ('ACCEPTED','CONFLICT','REJECTED')),
    server_table        text,
    server_id           uuid,
    error_code          text,
    error_message       text,
    processed_at        timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, batch_id, item_no),
    UNIQUE (company_id, client_txn_uuid),
    FOREIGN KEY (company_id, batch_id) REFERENCES sync.batches(company_id, id),
    CHECK ((result = 'ACCEPTED') = (server_id IS NOT NULL) OR result <> 'ACCEPTED')
);

-- Cases that need a person: a posting refused at sync, or a value that changed while offline.
CREATE TABLE sync.conflicts (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    store_id            uuid NOT NULL,
    device_id           uuid NOT NULL,
    client_txn_uuid     uuid NOT NULL,
    txn_type            text NOT NULL,
    category            text NOT NULL CHECK (category IN ('SALES','RETURNS','PAYMENTS','INVENTORY','PRODUCTS','PRICES',
                            'PROMOTIONS','CUSTOMERS','VENDORS','USERS','TRANSFERS','ACCOUNTING')),
    reason_code         text NOT NULL,
    payload             jsonb NOT NULL DEFAULT '{}'::jsonb,
    status              text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','RESOLVED','REJECTED')),
    opened_at           timestamptz NOT NULL DEFAULT now(),
    resolved_by         uuid,
    resolved_at         timestamptz,
    resolution_note     text,
    PRIMARY KEY (company_id, id),
    UNIQUE (company_id, client_txn_uuid, reason_code),
    FOREIGN KEY (company_id, store_id)  REFERENCES core.stores(company_id, id),
    FOREIGN KEY (company_id, device_id) REFERENCES core.devices(company_id, id),
    CHECK ((status = 'OPEN') = (resolved_at IS NULL)),
    CHECK (status = 'OPEN' OR (resolved_by IS NOT NULL AND btrim(coalesce(resolution_note, '')) <> ''))
);
CREATE INDEX ix_sync_conflicts_open ON sync.conflicts (company_id, store_id, opened_at) WHERE status = 'OPEN';

-- History of every conflict: opened, resolved, rejected. Never changed.
CREATE TABLE sync.conflict_events (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    conflict_id         uuid NOT NULL,
    event_type          text NOT NULL CHECK (event_type IN ('OPEN','RESOLVED','REJECTED')),
    actor_user_id       uuid,
    note                text,
    created_at          timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, conflict_id) REFERENCES sync.conflicts(company_id, id)
);
CREATE TRIGGER trg_no_change_conflict_events BEFORE UPDATE OR DELETE ON sync.conflict_events
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();

-- Pull position per device. It never moves backwards.
CREATE TABLE sync.checkpoints (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    device_id           uuid NOT NULL,
    pull_cursor         bigint NOT NULL DEFAULT 0 CHECK (pull_cursor >= 0),
    last_push_at        timestamptz,
    last_pull_at        timestamptz,
    updated_at          timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, device_id),
    FOREIGN KEY (company_id, device_id) REFERENCES core.devices(company_id, id)
);

-- Device audit trail: registered, authorized, revoked.
CREATE TABLE sync.device_events (
    company_id          uuid NOT NULL REFERENCES platform.companies(id),
    id                  uuid NOT NULL DEFAULT uuidv7(),
    device_id           uuid NOT NULL,
    event_type          text NOT NULL CHECK (event_type IN ('REGISTERED','AUTHORIZED','REVOKED')),
    actor_user_id       uuid,
    created_at          timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, id),
    FOREIGN KEY (company_id, device_id) REFERENCES core.devices(company_id, id)
);
CREATE TRIGGER trg_no_change_device_events BEFORE UPDATE OR DELETE ON sync.device_events
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();

CREATE FUNCTION sync.log_device_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO sync.device_events (company_id, device_id, event_type, actor_user_id)
        VALUES (NEW.company_id, NEW.id, 'REGISTERED', core.current_user_id());
    ELSIF NEW.is_active IS DISTINCT FROM OLD.is_active THEN
        INSERT INTO sync.device_events (company_id, device_id, event_type, actor_user_id)
        VALUES (NEW.company_id, NEW.id, CASE WHEN NEW.is_active THEN 'AUTHORIZED' ELSE 'REVOKED' END, core.current_user_id());
    END IF;
    RETURN NULL;
END $$;
CREATE TRIGGER trg_device_events AFTER INSERT OR UPDATE OF is_active ON core.devices
    FOR EACH ROW EXECUTE FUNCTION sync.log_device_change();

CREATE FUNCTION sync.log_conflict_event() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO sync.conflict_events (company_id, conflict_id, event_type, actor_user_id, note)
        VALUES (NEW.company_id, NEW.id, 'OPEN', NULL, NEW.reason_code);
    ELSE
        INSERT INTO sync.conflict_events (company_id, conflict_id, event_type, actor_user_id, note)
        VALUES (NEW.company_id, NEW.id, NEW.status, NEW.resolved_by, NEW.resolution_note);
    END IF;
    RETURN NULL;
END $$;
CREATE TRIGGER trg_conflict_event_open AFTER INSERT ON sync.conflicts
    FOR EACH ROW EXECUTE FUNCTION sync.log_conflict_event();
CREATE TRIGGER trg_conflict_event_change AFTER UPDATE OF status ON sync.conflicts
    FOR EACH ROW WHEN (OLD.status IS DISTINCT FROM NEW.status) EXECUTE FUNCTION sync.log_conflict_event();

SELECT core.enable_rls('sync.batches'::regclass, true);
SELECT core.enable_rls('sync.batch_items'::regclass, false);
SELECT core.enable_rls('sync.conflicts'::regclass, true);
SELECT core.enable_rls('sync.conflict_events'::regclass, false);
SELECT core.enable_rls('sync.checkpoints'::regclass, false);
SELECT core.enable_rls('sync.device_events'::regclass, false);

-- Opens a batch. Returns the batch id and whether it already existed (a repeated upload).
CREATE FUNCTION sync.open_batch(p_company uuid, p_device_uuid uuid, p_batch_uuid uuid, p_item_count int)
RETURNS TABLE (out_batch_id uuid, out_is_duplicate boolean)
LANGUAGE plpgsql AS $$
DECLARE
    d    core.devices%ROWTYPE;
    v_id uuid;
BEGIN
    SELECT * INTO d FROM core.devices WHERE company_id = p_company AND device_uuid = p_device_uuid;
    IF NOT FOUND OR NOT d.is_active THEN
        RAISE EXCEPTION 'device is not registered or not authorized' USING ERRCODE = 'insufficient_privilege';
    END IF;
    SELECT b.id INTO v_id FROM sync.batches b
     WHERE b.company_id = p_company AND b.device_id = d.id AND b.batch_uuid = p_batch_uuid;
    IF FOUND THEN
        RETURN QUERY SELECT v_id, true;
        RETURN;
    END IF;
    INSERT INTO sync.batches (company_id, store_id, device_id, batch_uuid, item_count)
    VALUES (p_company, d.store_id, d.id, p_batch_uuid, p_item_count)
    RETURNING id INTO v_id;
    UPDATE sync.checkpoints SET last_push_at = now(), updated_at = now()
     WHERE company_id = p_company AND device_id = d.id;
    RETURN QUERY SELECT v_id, false;
END $$;

-- Records the result of one transaction. Returns the stored result and whether this call recorded it.
-- A transaction already recorded (in any batch) returns its earlier result and is_new = false.
CREATE FUNCTION sync.record_item(p_company uuid, p_batch uuid, p_item_no int, p_client_uuid uuid,
                                 p_txn_type text, p_result text,
                                 p_server_table text DEFAULT NULL, p_server_id uuid DEFAULT NULL,
                                 p_error_code text DEFAULT NULL, p_error_message text DEFAULT NULL)
RETURNS TABLE (out_result text, out_is_new boolean)
LANGUAGE plpgsql AS $$
DECLARE v_result text;
BEGIN
    INSERT INTO sync.batch_items (company_id, batch_id, item_no, client_txn_uuid, txn_type, result,
                                  server_table, server_id, error_code, error_message)
    VALUES (p_company, p_batch, p_item_no, p_client_uuid, p_txn_type, p_result,
            p_server_table, p_server_id, p_error_code, p_error_message)
    ON CONFLICT (company_id, client_txn_uuid) DO NOTHING
    RETURNING result INTO v_result;
    IF FOUND THEN
        RETURN QUERY SELECT p_result, true;
        RETURN;
    END IF;
    SELECT i.result INTO v_result FROM sync.batch_items i
     WHERE i.company_id = p_company AND i.client_txn_uuid = p_client_uuid;
    RETURN QUERY SELECT v_result, false;
END $$;

-- Opens a conflict for a refused transaction. Returns the conflict id (existing id on a repeat).
CREATE FUNCTION sync.open_conflict(p_company uuid, p_device_uuid uuid, p_client_uuid uuid, p_txn_type text,
                                   p_category text, p_reason text, p_payload jsonb DEFAULT '{}'::jsonb)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    d    core.devices%ROWTYPE;
    v_id uuid;
BEGIN
    SELECT * INTO d FROM core.devices WHERE company_id = p_company AND device_uuid = p_device_uuid;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'device is not registered' USING ERRCODE = 'insufficient_privilege';
    END IF;
    INSERT INTO sync.conflicts (company_id, store_id, device_id, client_txn_uuid, txn_type, category, reason_code, payload)
    VALUES (p_company, d.store_id, d.id, p_client_uuid, p_txn_type, p_category, p_reason, COALESCE(p_payload, '{}'::jsonb))
    ON CONFLICT (company_id, client_txn_uuid, reason_code) DO NOTHING
    RETURNING id INTO v_id;
    IF v_id IS NULL THEN
        SELECT c.id INTO v_id FROM sync.conflicts c
         WHERE c.company_id = p_company AND c.client_txn_uuid = p_client_uuid AND c.reason_code = p_reason;
    END IF;
    RETURN v_id;
END $$;

-- Decides a conflict. A note is required. Needs SYNC_CONFLICT_RESOLVE.
CREATE FUNCTION sync.resolve_conflict(p_company uuid, p_conflict uuid, p_decision text, p_note text)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    v_user   uuid := core.current_user_id();
    v_status text;
BEGIN
    PERFORM sec.require_permission(p_company, v_user, 'SYNC_CONFLICT_RESOLVE');
    IF p_decision NOT IN ('RESOLVED','REJECTED') THEN
        RAISE EXCEPTION 'decision must be RESOLVED or REJECTED' USING ERRCODE = 'check_violation';
    END IF;
    IF btrim(coalesce(p_note, '')) = '' THEN
        RAISE EXCEPTION 'a note is required to decide a conflict' USING ERRCODE = 'check_violation';
    END IF;
    SELECT status INTO v_status FROM sync.conflicts WHERE company_id = p_company AND id = p_conflict FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'conflict % not found', p_conflict USING ERRCODE = 'no_data_found';
    END IF;
    IF v_status <> 'OPEN' THEN
        RAISE EXCEPTION 'conflict % is already %', p_conflict, v_status USING ERRCODE = 'check_violation';
    END IF;
    UPDATE sync.conflicts SET status = p_decision, resolved_by = v_user, resolved_at = now(), resolution_note = p_note
     WHERE company_id = p_company AND id = p_conflict;
END $$;

-- Saves the pull position. Refuses a position that moves backwards.
CREATE FUNCTION sync.save_checkpoint(p_company uuid, p_device_uuid uuid, p_pull_cursor bigint)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    d   core.devices%ROWTYPE;
    cur bigint;
BEGIN
    SELECT * INTO d FROM core.devices WHERE company_id = p_company AND device_uuid = p_device_uuid;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'device is not registered' USING ERRCODE = 'insufficient_privilege';
    END IF;
    SELECT pull_cursor INTO cur FROM sync.checkpoints WHERE company_id = p_company AND device_id = d.id;
    IF cur IS NOT NULL AND p_pull_cursor < cur THEN
        RAISE EXCEPTION 'checkpoint cannot move back from % to %', cur, p_pull_cursor USING ERRCODE = 'check_violation';
    END IF;
    INSERT INTO sync.checkpoints (company_id, device_id, pull_cursor, last_pull_at, updated_at)
    VALUES (p_company, d.id, p_pull_cursor, now(), now())
    ON CONFLICT (company_id, device_id)
    DO UPDATE SET pull_cursor = EXCLUDED.pull_cursor, last_pull_at = now(), updated_at = now();
END $$;

-- Closes a batch: counts its results and marks it COMPLETED, or INCOMPLETE if items are missing.
CREATE FUNCTION sync.complete_batch(p_company uuid, p_batch uuid) RETURNS text
LANGUAGE plpgsql AS $$
DECLARE
    b       sync.batches%ROWTYPE;
    v_items int;
    v_acc   int;
    v_conf  int;
    v_rej   int;
    v_state text;
BEGIN
    SELECT * INTO b FROM sync.batches WHERE company_id = p_company AND id = p_batch FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'batch % not found', p_batch USING ERRCODE = 'no_data_found';
    END IF;
    SELECT count(*), count(*) FILTER (WHERE result = 'ACCEPTED'),
           count(*) FILTER (WHERE result = 'CONFLICT'), count(*) FILTER (WHERE result = 'REJECTED')
      INTO v_items, v_acc, v_conf, v_rej
      FROM sync.batch_items WHERE company_id = p_company AND batch_id = p_batch;
    v_state := CASE WHEN v_items = b.item_count THEN 'COMPLETED' ELSE 'INCOMPLETE' END;
    UPDATE sync.batches
       SET status = v_state, completed_at = now(),
           accepted_count = v_acc, conflict_count = v_conf, rejected_count = v_rej
     WHERE company_id = p_company AND id = p_batch;
    RETURN v_state;
END $$;

-- Status per device for the manager and for central monitoring (Part 23.4, report "Offline Synchronization Status").
CREATE VIEW rpt.v_sync_device_status WITH (security_invoker = true) AS
SELECT d.company_id, d.store_id, d.id AS device_id, d.label, d.is_active, d.last_seen_at,
       (SELECT max(b.received_at) FROM sync.batches b WHERE b.company_id = d.company_id AND b.device_id = d.id) AS last_batch_at,
       (SELECT k.pull_cursor FROM sync.checkpoints k WHERE k.company_id = d.company_id AND k.device_id = d.id) AS pull_cursor,
       (SELECT count(*) FROM sync.conflicts c WHERE c.company_id = d.company_id AND c.device_id = d.id AND c.status = 'OPEN') AS open_conflicts,
       (SELECT count(*) FROM sync.batch_items i JOIN sync.batches b ON b.company_id = i.company_id AND b.id = i.batch_id
         WHERE b.company_id = d.company_id AND b.device_id = d.id AND i.result = 'REJECTED'
           AND i.processed_at > now() - interval '24 hours') AS rejected_last_24h
  FROM core.devices d;
GRANT SELECT ON rpt.v_sync_device_status TO pos_app;

GRANT SELECT, INSERT, UPDATE ON sync.batches, sync.batch_items, sync.conflicts, sync.checkpoints TO pos_app;
GRANT SELECT, INSERT ON sync.conflict_events, sync.device_events TO pos_app;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA sync TO pos_app;
GRANT EXECUTE ON FUNCTION sync.open_batch(uuid, uuid, uuid, int) TO pos_app;
GRANT EXECUTE ON FUNCTION sync.record_item(uuid, uuid, int, uuid, text, text, text, uuid, text, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sync.open_conflict(uuid, uuid, uuid, text, text, text, jsonb) TO pos_app;
GRANT EXECUTE ON FUNCTION sync.resolve_conflict(uuid, uuid, text, text) TO pos_app;
GRANT EXECUTE ON FUNCTION sync.save_checkpoint(uuid, uuid, bigint) TO pos_app;
GRANT EXECUTE ON FUNCTION sync.complete_batch(uuid, uuid) TO pos_app;

-- =====================================================================
-- V6: BACKUP AND RECOVERY RECORDS (Parts 7, 30, 31, 36 item 24)
-- The backup itself runs outside the database (see the design document):
-- base backups plus continuous WAL archiving, and a logical export per company.
-- These tables record each run and each restore drill, so that the health of
-- backups is visible. They are system-level, not tenant data: the application
-- role has no access. Thresholds in ops.v_backup_health are proposals.
-- =====================================================================

CREATE TABLE ops.backup_runs (
    id                  uuid NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    kind                text NOT NULL CHECK (kind IN ('BASE','WAL_ARCHIVE','LOGICAL_EXPORT')),
    company_id          uuid REFERENCES platform.companies(id),   -- set for LOGICAL_EXPORT only
    started_at          timestamptz NOT NULL DEFAULT now(),
    finished_at         timestamptz,
    status              text NOT NULL DEFAULT 'RUNNING' CHECK (status IN ('RUNNING','OK','FAILED')),
    storage_location    text NOT NULL,                              -- off-site location, no secrets
    size_bytes          bigint CHECK (size_bytes IS NULL OR size_bytes >= 0),
    sha256              text CHECK (sha256 IS NULL OR sha256 ~ '^[0-9a-f]{64}$'),
    encrypted           boolean NOT NULL DEFAULT true,
    error_message       text,
    CHECK (finished_at IS NULL OR finished_at >= started_at),
    CHECK (status = 'RUNNING' OR finished_at IS NOT NULL),
    CHECK (status <> 'OK' OR (sha256 IS NOT NULL AND size_bytes IS NOT NULL AND encrypted)),
    CHECK (kind <> 'LOGICAL_EXPORT' OR company_id IS NOT NULL)
);

CREATE TABLE ops.restore_drills (
    id                      uuid NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    backup_run_id           uuid NOT NULL REFERENCES ops.backup_runs(id),
    started_at              timestamptz NOT NULL DEFAULT now(),
    finished_at             timestamptz NOT NULL,
    outcome                 text NOT NULL CHECK (outcome IN ('PASS','FAIL')),
    measured_rto_minutes    numeric(10,2) CHECK (measured_rto_minutes IS NULL OR measured_rto_minutes >= 0),
    measured_rpo_minutes    numeric(10,2) CHECK (measured_rpo_minutes IS NULL OR measured_rpo_minutes >= 0),
    rows_checked            bigint CHECK (rows_checked IS NULL OR rows_checked >= 0),
    checks_performed        text NOT NULL,                          -- what was compared, e.g. trial balance totals
    performed_by            text NOT NULL,
    CHECK (finished_at >= started_at)
);

CREATE TRIGGER trg_no_change_drills BEFORE UPDATE OR DELETE ON ops.restore_drills
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();

CREATE VIEW ops.v_backup_health AS
WITH b AS (
    SELECT
        max(finished_at) FILTER (WHERE kind = 'BASE' AND status = 'OK')            AS last_base_at,
        max(finished_at) FILTER (WHERE kind = 'WAL_ARCHIVE' AND status = 'OK')     AS last_wal_at,
        max(finished_at) FILTER (WHERE kind = 'LOGICAL_EXPORT' AND status = 'OK')  AS last_export_at,
        count(*) FILTER (WHERE status = 'FAILED' AND started_at > now() - interval '7 days') AS failed_last_7d
      FROM ops.backup_runs
),
d AS (
    SELECT max(finished_at) FILTER (WHERE outcome = 'PASS') AS last_pass_at,
           max(finished_at) AS last_drill_at
      FROM ops.restore_drills
)
SELECT b.last_base_at, b.last_wal_at, b.last_export_at, b.failed_last_7d,
       d.last_drill_at, d.last_pass_at,
       CASE WHEN b.last_base_at IS NULL THEN 'NO_BASE_BACKUP'
            WHEN now() - b.last_base_at > interval '7 days' THEN 'BASE_BACKUP_STALE'
            ELSE 'OK' END AS base_status,
       CASE WHEN b.last_wal_at IS NULL THEN 'NO_WAL_ARCHIVE'
            WHEN now() - b.last_wal_at > interval '15 minutes' THEN 'WAL_ARCHIVE_STALE'
            ELSE 'OK' END AS wal_status,
       CASE WHEN d.last_pass_at IS NULL THEN 'NEVER_TESTED'
            WHEN now() - d.last_pass_at > interval '90 days' THEN 'RESTORE_TEST_DUE'
            ELSE 'OK' END AS restore_test_status
  FROM b CROSS JOIN d;

-- System tables: no access for the application role.
REVOKE ALL ON ALL TABLES IN SCHEMA ops FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA ops FROM pos_app;

-- End of FootwearPOS v6 schema.


-- ===== 0003_platform_admin_and_users.sql =====
-- 0003: platform administrators, company creation through the platform, and user-creation support.
-- Apply once, as the database owner (postgres), after the v6 schema:
--   psql -h localhost -U postgres -d E-Footwear -v ON_ERROR_STOP=1 -f db/migrations/0003_platform_admin_and_users.sql

-- ---------------------------------------------------------------------
-- 1. Platform administrators. They are NOT company users: they have no company, and they
--    sign in to a separate token space ("admin.<secret>"). Created by the operator CLI only.
-- ---------------------------------------------------------------------
CREATE TABLE platform.admins (
    id              uuid NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    username        text NOT NULL CHECK (btrim(username) <> ''),
    password_hash   text NOT NULL,                       -- argon2id, produced by the application
    full_name       text NOT NULL,
    status          text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','DISABLED')),
    failed_logins   int NOT NULL DEFAULT 0 CHECK (failed_logins >= 0),
    locked_until    timestamptz,
    last_login_at   timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_platform_admins_username ON platform.admins (lower(username));

CREATE TABLE platform.admin_sessions (
    id              uuid NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    admin_id        uuid NOT NULL REFERENCES platform.admins(id),
    token_hash      text NOT NULL UNIQUE,
    created_at      timestamptz NOT NULL DEFAULT now(),
    expires_at      timestamptz NOT NULL,
    idle_expires_at timestamptz NOT NULL,
    revoked_at      timestamptz,
    ip              inet,
    user_agent      text,
    CHECK (idle_expires_at <= expires_at)
);
CREATE INDEX ix_admin_sessions_admin ON platform.admin_sessions (admin_id) WHERE revoked_at IS NULL;

-- Append-only record of what platform administrators did.
CREATE TABLE platform.admin_actions (
    id              uuid NOT NULL DEFAULT uuidv7() PRIMARY KEY,
    admin_id        uuid NOT NULL REFERENCES platform.admins(id),
    action          text NOT NULL CHECK (action IN ('LOGIN','COMPANY_CREATED','STORE_CREATED','USER_CREATED')),
    company_id      uuid REFERENCES platform.companies(id),
    detail          jsonb NOT NULL DEFAULT '{}'::jsonb,
    occurred_at     timestamptz NOT NULL DEFAULT now()
);
CREATE TRIGGER trg_no_change_admin_actions BEFORE UPDATE OR DELETE ON platform.admin_actions
    FOR EACH ROW EXECUTE FUNCTION core.forbid_update_delete();

-- ---------------------------------------------------------------------
-- 2. Companies: tenants may read only their own company row; platform administrators read all.
--    Nobody but the functions below inserts companies (fixes the earlier cross-tenant read/write hole).
-- ---------------------------------------------------------------------
ALTER TABLE platform.companies ENABLE ROW LEVEL SECURITY;
CREATE POLICY company_read ON platform.companies FOR SELECT
    USING (id = core.current_company_id()
           OR COALESCE(current_setting('app.platform_admin', true), '') = 'on');
REVOKE INSERT, UPDATE, DELETE ON platform.companies FROM pos_app;

-- ---------------------------------------------------------------------
-- 3. Platform administrator login and sessions (same rules as company login: lockout, idle and absolute expiry).
--    Policy values: 5 failed attempts, 15 minute lock, 30 minute idle timeout, 12 hour absolute limit.
-- ---------------------------------------------------------------------
CREATE FUNCTION platform.admin_login(p_username text, p_password_ok boolean,
                                     p_ip inet DEFAULT NULL, p_user_agent text DEFAULT NULL)
RETURNS TABLE (out_outcome text, out_admin_id uuid, out_session_token text, out_locked_until timestamptz)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    a       platform.admins%ROWTYPE;
    v_token text;
    v_fails int;
BEGIN
    SELECT * INTO a FROM platform.admins WHERE lower(username) = lower(p_username) FOR UPDATE;
    IF NOT FOUND OR a.status <> 'ACTIVE' THEN
        RETURN QUERY SELECT 'INVALID'::text, NULL::uuid, NULL::text, NULL::timestamptz;
        RETURN;
    END IF;
    IF a.locked_until IS NOT NULL AND a.locked_until > now() THEN
        RETURN QUERY SELECT 'LOCKED'::text, a.id, NULL::text, a.locked_until;
        RETURN;
    END IF;
    IF NOT p_password_ok THEN
        v_fails := a.failed_logins + 1;
        IF v_fails >= 5 THEN
            UPDATE platform.admins SET failed_logins = 0, locked_until = now() + interval '15 minutes' WHERE id = a.id;
            RETURN QUERY SELECT 'LOCKED'::text, a.id, NULL::text, now() + interval '15 minutes';
            RETURN;
        END IF;
        UPDATE platform.admins SET failed_logins = v_fails WHERE id = a.id;
        RETURN QUERY SELECT 'INVALID'::text, NULL::uuid, NULL::text, NULL::timestamptz;
        RETURN;
    END IF;

    v_token := sec.new_token();
    INSERT INTO platform.admin_sessions (admin_id, token_hash, expires_at, idle_expires_at, ip, user_agent)
    VALUES (a.id, sec.token_hash(v_token), now() + interval '12 hours', now() + interval '30 minutes', p_ip, p_user_agent);
    UPDATE platform.admins SET failed_logins = 0, locked_until = NULL, last_login_at = now() WHERE id = a.id;
    INSERT INTO platform.admin_actions (admin_id, action) VALUES (a.id, 'LOGIN');
    RETURN QUERY SELECT 'OK'::text, a.id, v_token, NULL::timestamptz;
END $$;

CREATE FUNCTION platform.admin_session(p_token text)
RETURNS TABLE (out_admin_id uuid, out_username text, out_full_name text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    s platform.admin_sessions%ROWTYPE;
    a platform.admins%ROWTYPE;
BEGIN
    SELECT * INTO s FROM platform.admin_sessions WHERE token_hash = sec.token_hash(p_token);
    IF NOT FOUND OR s.revoked_at IS NOT NULL OR now() >= s.expires_at OR now() >= s.idle_expires_at THEN
        RAISE EXCEPTION 'administrator session is missing, expired or revoked'
            USING ERRCODE = 'invalid_authorization_specification';
    END IF;
    SELECT * INTO a FROM platform.admins WHERE id = s.admin_id;
    IF a.status <> 'ACTIVE' THEN
        RAISE EXCEPTION 'administrator account is disabled' USING ERRCODE = 'invalid_authorization_specification';
    END IF;
    UPDATE platform.admin_sessions
       SET idle_expires_at = LEAST(expires_at, now() + interval '30 minutes')
     WHERE id = s.id;
    RETURN QUERY SELECT a.id, a.username, a.full_name;
END $$;

CREATE FUNCTION platform.admin_logout(p_token text) RETURNS void
LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
    UPDATE platform.admin_sessions SET revoked_at = now()
     WHERE token_hash = sec.token_hash(p_token) AND revoked_at IS NULL
$$;

-- ---------------------------------------------------------------------
-- 4. Company creation. Creates the company (the schema trigger adds head office, channels and the chart of
--    accounts), its first store, and its owner (must change the temporary password at first sign-in).
--    Runs as the owner so that pos_app does not need INSERT on platform.companies.
-- ---------------------------------------------------------------------
CREATE FUNCTION platform.create_company(p_admin_id uuid, p_name text, p_timezone text,
                                        p_owner_username text, p_owner_full_name text, p_owner_hash text,
                                        p_store_code text, p_store_name text)
RETURNS TABLE (out_company_id uuid, out_store_id uuid, out_owner_id uuid)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_company uuid;
    v_store   uuid;
    v_owner   uuid;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM platform.admins WHERE id = p_admin_id AND status = 'ACTIVE') THEN
        RAISE EXCEPTION 'platform administrator % is not active', p_admin_id USING ERRCODE = 'insufficient_privilege';
    END IF;
    IF btrim(coalesce(p_name, '')) = '' OR btrim(coalesce(p_owner_username, '')) = ''
       OR btrim(coalesce(p_store_code, '')) = '' OR btrim(coalesce(p_store_name, '')) = '' THEN
        RAISE EXCEPTION 'company name, owner username and first store are required' USING ERRCODE = 'check_violation';
    END IF;

    -- platform plan; limits are placeholders until the owner defines real plans
    INSERT INTO platform.plans (code, name, max_stores, max_users)
    VALUES ('STANDARD', 'Standard', 1000, 10000)
    ON CONFLICT (code) DO NOTHING;

    INSERT INTO platform.companies (name, timezone, plan_code)
    VALUES (btrim(p_name), COALESCE(NULLIF(btrim(p_timezone), ''), 'Asia/Karachi'), 'STANDARD')
    RETURNING id INTO v_company;

    PERFORM set_config('app.company_id', v_company::text, true);
    PERFORM set_config('app.all_stores', 'on', true);

    INSERT INTO core.stores (company_id, code, name)
    VALUES (v_company, btrim(p_store_code), btrim(p_store_name))
    RETURNING id INTO v_store;

    INSERT INTO sec.users (company_id, username, password_hash, full_name, role_code, all_stores, must_change_password)
    VALUES (v_company, btrim(p_owner_username), p_owner_hash, btrim(p_owner_full_name), 'COMPANY_OWNER', true, true)
    RETURNING id INTO v_owner;

    INSERT INTO platform.admin_actions (admin_id, action, company_id, detail)
    VALUES (p_admin_id, 'COMPANY_CREATED', v_company,
            jsonb_build_object('owner', btrim(p_owner_username), 'firstStore', btrim(p_store_code)));

    RETURN QUERY SELECT v_company, v_store, v_owner;
END $$;

-- ---------------------------------------------------------------------
-- 5. Grants. Functions are callable by the API role only.
-- ---------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE ON platform.admins, platform.admin_sessions TO pos_app;
GRANT SELECT, INSERT ON platform.admin_actions TO pos_app;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA platform TO pos_app;

REVOKE ALL ON FUNCTION platform.admin_login(text, boolean, inet, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION platform.admin_session(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION platform.admin_logout(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION platform.create_company(uuid, text, text, text, text, text, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION platform.admin_login(text, boolean, inet, text) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.admin_session(text) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.admin_logout(text) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.create_company(uuid, text, text, text, text, text, text, text) TO pos_app;


-- ===== 0005_user_email.sql =====
-- 0005: every company user has an email. The email is the sign-in name and is unique across ALL companies,
-- so it alone identifies the person, their company and their role (no company field at sign-in).
--   psql -h localhost -U postgres -d E-Footwear -v ON_ERROR_STOP=1 -f db/migrations/0005_user_email.sql

ALTER TABLE sec.users ADD COLUMN email text;

-- Existing dev users get a placeholder address built from their username. Change them if you want real ones.
UPDATE sec.users SET email = lower(username) || '.c' || company_id || '@example.test' WHERE email IS NULL;

ALTER TABLE sec.users ALTER COLUMN email SET NOT NULL;
ALTER TABLE sec.users ADD CONSTRAINT users_email_format_chk CHECK (email ~ '^[^@\s]+@[^@\s]+\.[^@\s]+$');
CREATE UNIQUE INDEX ux_users_email ON sec.users (lower(email)) WHERE NOT is_deleted;

-- Before sign-in no company is selected, so row-level security hides every user. These two functions run as the
-- owner and reveal only what sign-in and user creation need: a company id, and whether an email is taken.
CREATE FUNCTION platform.find_company_by_user_email(p_email text) RETURNS uuid
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, public AS $$
    SELECT company_id FROM sec.users WHERE lower(email) = lower(btrim(p_email)) AND NOT is_deleted
$$;

CREATE FUNCTION platform.email_in_use(p_email text) RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, public AS $$
    SELECT EXISTS (SELECT 1 FROM sec.users WHERE lower(email) = lower(btrim(p_email)) AND NOT is_deleted)
$$;

REVOKE ALL ON FUNCTION platform.find_company_by_user_email(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION platform.email_in_use(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION platform.find_company_by_user_email(text) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.email_in_use(text) TO pos_app;

-- Company creation takes the owner's email instead of a username (the email is stored as the username too).
DROP FUNCTION platform.create_company(uuid, text, text, text, text, text, text, text);
CREATE FUNCTION platform.create_company(p_admin_id uuid, p_name text, p_timezone text,
                                        p_owner_email text, p_owner_full_name text, p_owner_hash text,
                                        p_store_code text, p_store_name text)
RETURNS TABLE (out_company_id uuid, out_store_id uuid, out_owner_id uuid)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_company uuid;
    v_store   uuid;
    v_owner   uuid;
    v_email   text := lower(btrim(coalesce(p_owner_email, '')));
BEGIN
    IF NOT EXISTS (SELECT 1 FROM platform.admins WHERE id = p_admin_id AND status = 'ACTIVE') THEN
        RAISE EXCEPTION 'platform administrator % is not active', p_admin_id USING ERRCODE = 'insufficient_privilege';
    END IF;
    IF btrim(coalesce(p_name, '')) = '' OR v_email = ''
       OR btrim(coalesce(p_store_code, '')) = '' OR btrim(coalesce(p_store_name, '')) = '' THEN
        RAISE EXCEPTION 'company name, owner email and first store are required' USING ERRCODE = 'check_violation';
    END IF;

    INSERT INTO platform.plans (code, name, max_stores, max_users)
    VALUES ('STANDARD', 'Standard', 1000, 10000)
    ON CONFLICT (code) DO NOTHING;

    INSERT INTO platform.companies (name, timezone, plan_code)
    VALUES (btrim(p_name), COALESCE(NULLIF(btrim(p_timezone), ''), 'Asia/Karachi'), 'STANDARD')
    RETURNING id INTO v_company;

    PERFORM set_config('app.company_id', v_company::text, true);
    PERFORM set_config('app.all_stores', 'on', true);

    INSERT INTO core.stores (company_id, code, name)
    VALUES (v_company, btrim(p_store_code), btrim(p_store_name))
    RETURNING id INTO v_store;

    INSERT INTO sec.users (company_id, username, email, password_hash, full_name, role_code, all_stores, must_change_password)
    VALUES (v_company, v_email, v_email, p_owner_hash, btrim(p_owner_full_name), 'COMPANY_OWNER', true, true)
    RETURNING id INTO v_owner;

    INSERT INTO platform.admin_actions (admin_id, action, company_id, detail)
    VALUES (p_admin_id, 'COMPANY_CREATED', v_company,
            jsonb_build_object('owner', v_email, 'firstStore', btrim(p_store_code)));

    RETURN QUERY SELECT v_company, v_store, v_owner;
END $$;

REVOKE ALL ON FUNCTION platform.create_company(uuid, text, text, text, text, text, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION platform.create_company(uuid, text, text, text, text, text, text, text) TO pos_app;

COMMIT;
