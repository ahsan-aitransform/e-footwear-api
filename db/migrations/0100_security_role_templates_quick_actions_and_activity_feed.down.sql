-- Rolls back 0100. Role templates, quick actions and the activity feed view are removed; the grants are revoked; the
-- row-change audit function is restored to the baseline version (without the token_hash exclusion).
DROP VIEW IF EXISTS audit.v_user_activity_feed;

REVOKE DELETE ON sec.user_permissions, sec.user_stores FROM pos_app;

DROP TABLE IF EXISTS sec.user_quick_actions;
DROP TABLE IF EXISTS sec.role_template_permissions;
DROP TABLE IF EXISTS sec.role_templates;

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
