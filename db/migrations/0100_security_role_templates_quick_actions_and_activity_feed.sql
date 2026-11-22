-- 0100: Security module (docx 1.1 to 1.4, Part 6, Part 29).
--   * role templates: named sets of permission codes that PREFILL a user's permissions (a template never blocks an override);
--   * the quick-action toolbar of the original "Standard Menu" (which buttons a user sees);
--   * the grants the application role needs to REPLACE a user's permissions and store assignments (DELETE on those two config
--     tables; they are not ledgers, every change is written to the activity log by the application);
--   * the Users Activity feed: one read-only view over data changes (audit.activity_log) and sign-in events (sec.login_attempts);
--   * the row-change audit function no longer copies token hashes (device tokens) into the activity log.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

CREATE TABLE sec.role_templates (
    company_id  uuid NOT NULL REFERENCES platform.companies(id),
    id          uuid NOT NULL DEFAULT uuidv7(),
    name        text NOT NULL CHECK (btrim(name) <> '' AND length(name) <= 80),
    description text CHECK (description IS NULL OR length(description) <= 400),
    created_at  timestamptz NOT NULL DEFAULT now(),
    created_by  uuid,
    updated_at  timestamptz,
    updated_by  uuid,
    row_ver     bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, id)
);
CREATE UNIQUE INDEX ux_role_templates_name ON sec.role_templates (company_id, lower(name));
CREATE TRIGGER trg_touch BEFORE UPDATE ON sec.role_templates FOR EACH ROW EXECUTE FUNCTION core.touch_row();
CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON sec.role_templates
    FOR EACH ROW EXECUTE FUNCTION audit.log_row_change();

CREATE TABLE sec.role_template_permissions (
    company_id      uuid NOT NULL,
    template_id     uuid NOT NULL,
    permission_code text NOT NULL REFERENCES sec.permissions(code),
    PRIMARY KEY (company_id, template_id, permission_code),
    FOREIGN KEY (company_id, template_id) REFERENCES sec.role_templates(company_id, id) ON DELETE CASCADE
);

CREATE TABLE sec.user_quick_actions (
    company_id  uuid NOT NULL,
    user_id     uuid NOT NULL,
    action_code text NOT NULL CHECK (action_code ~ '^[A-Z0-9_]{1,40}$'),
    sort_order  integer NOT NULL DEFAULT 0,
    PRIMARY KEY (company_id, user_id, action_code),
    FOREIGN KEY (company_id, user_id) REFERENCES sec.users(company_id, id)
);

SELECT core.enable_rls('sec.role_templates'::regclass, false);
SELECT core.enable_rls('sec.role_template_permissions'::regclass, false);
SELECT core.enable_rls('sec.user_quick_actions'::regclass, false);

GRANT SELECT, INSERT, UPDATE, DELETE ON sec.role_templates, sec.role_template_permissions, sec.user_quick_actions TO pos_app;
GRANT DELETE ON sec.user_permissions, sec.user_stores TO pos_app;

-- Row-change log: never copy a password hash or a token hash into the activity log.
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
            CASE WHEN TG_OP IN ('UPDATE','DELETE') THEN to_jsonb(OLD) - 'password_hash' - 'token_hash' END,
            CASE WHEN TG_OP IN ('INSERT','UPDATE') THEN to_jsonb(NEW) - 'password_hash' - 'token_hash' END);
    RETURN NULL;
END $$;

-- Users Activity feed (docx 1.4): data changes and sign-in events in one list, newest first. security_invoker = true, so the
-- caller's row-level security applies: a user only ever sees rows of their own company.
CREATE VIEW audit.v_user_activity_feed WITH (security_invoker = true) AS
SELECT feed.company_id, feed.entry_id, feed.occurred_at, feed.user_id,
       u.full_name AS user_full_name, u.email AS user_email,
       feed.store_id, feed.entry_kind, feed.action, feed.entity_type, feed.entity_id, feed.remarks,
       feed.ip_address, feed.device_label, feed.old_values_json, feed.new_values_json
  FROM (
        SELECT a.company_id, a.id AS entry_id, a.occurred_at, a.user_id, a.store_id,
               'BUSINESS_CHANGE'::text AS entry_kind, a.action, a.entity_type, a.entity_id, a.remarks,
               NULL::inet AS ip_address, NULL::text AS device_label,
               a.old_values::text AS old_values_json, a.new_values::text AS new_values_json
          FROM audit.activity_log a
        UNION ALL
        SELECT l.company_id, l.id, l.attempted_at, l.user_id, NULL::uuid,
               'SIGN_IN'::text, l.outcome, 'sec.login_attempts'::text, l.user_id::text,
               CASE WHEN l.acted_by IS NOT NULL THEN 'by ' || l.acted_by::text END,
               l.ip, l.device_label, NULL::text, NULL::text
          FROM sec.login_attempts l
       ) feed
  LEFT JOIN sec.users u ON u.company_id = feed.company_id AND u.id = feed.user_id;

GRANT SELECT ON audit.v_user_activity_feed TO pos_app;
