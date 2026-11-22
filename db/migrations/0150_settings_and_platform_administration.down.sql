-- Rolls back 0150. Rows that use the new admin actions or settings are kept only until the tables are dropped.
-- The restored admin_actions constraint is NOT VALID, so existing rows with the newer action names stay readable
-- (the table is append-only and cannot be edited); new rows must use the original four actions.

DROP FUNCTION IF EXISTS ops.list_restore_drills(int);
DROP FUNCTION IF EXISTS ops.list_company_exports(int);
DROP FUNCTION IF EXISTS ops.list_backup_runs(int);
DROP FUNCTION IF EXISTS ops.read_backup_health();

DROP TRIGGER IF EXISTS trg_user_plan_limit ON sec.users;
DROP FUNCTION IF EXISTS sec.enforce_user_limit();
DROP TRIGGER IF EXISTS trg_store_plan_limit ON core.stores;
DROP FUNCTION IF EXISTS core.enforce_store_limit();

DROP FUNCTION IF EXISTS platform.update_company_profile(uuid, jsonb, bigint);

DROP TRIGGER IF EXISTS trg_company_must_be_active ON sec.sessions;
DROP FUNCTION IF EXISTS sec.refuse_sessions_for_inactive_company();
DROP FUNCTION IF EXISTS platform.set_company_status(uuid, uuid, text);

DROP FUNCTION IF EXISTS platform.assign_company_plan(uuid, uuid, text);
DROP FUNCTION IF EXISTS platform.update_plan(uuid, text, text, int, int, jsonb, boolean);
DROP FUNCTION IF EXISTS platform.create_plan(uuid, text, text, int, int, jsonb);
DROP FUNCTION IF EXISTS platform.require_active_administrator(uuid);
GRANT INSERT, UPDATE ON platform.plans TO pos_app;
ALTER TABLE platform.plans DROP COLUMN IF EXISTS is_active;

ALTER TABLE platform.admin_actions DROP CONSTRAINT IF EXISTS admin_actions_action_check;
ALTER TABLE platform.admin_actions ADD CONSTRAINT admin_actions_action_check
    CHECK (action IN ('LOGIN', 'COMPANY_CREATED', 'STORE_CREATED', 'USER_CREATED')) NOT VALID;

DROP TABLE IF EXISTS core.user_preferences;
DROP TABLE IF EXISTS core.store_settings;
DROP TABLE IF EXISTS core.company_settings;
