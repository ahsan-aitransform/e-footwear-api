-- 0150: settings storage, platform plan administration, company suspension, plan limits, backup status reading.
-- Module: Settings and Platform administration (Phase 0 rows ORG-2.19, ORG-2.20, ADD-05-xx, ADD-07-xx).
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

-- ---------------------------------------------------------------------
-- 1. Settings storage. A section is one named JSON document with a row version.
--    core.company_settings: company-wide values, readable by every user of the company (RLS tenant isolation only,
--    so a cashier can read the receipt layout). core.store_settings: per-store overrides (company + store RLS).
--    core.user_preferences: one row per user, visible only to that user.
-- ---------------------------------------------------------------------
CREATE TABLE core.company_settings (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    section_code    text NOT NULL CHECK (section_code ~ '^[A-Z][A-Z0-9_]{1,59}$'),
    setting_values  jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(setting_values) IN ('object', 'array')),
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid DEFAULT core.current_user_id(),
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, section_code)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON core.company_settings FOR EACH ROW EXECUTE FUNCTION core.touch_row();
CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON core.company_settings
    FOR EACH ROW EXECUTE FUNCTION audit.log_row_change();

CREATE TABLE core.store_settings (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    store_id        uuid NOT NULL,
    section_code    text NOT NULL CHECK (section_code ~ '^[A-Z][A-Z0-9_]{1,59}$'),
    setting_values  jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(setting_values) IN ('object', 'array')),
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid DEFAULT core.current_user_id(),
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, store_id, section_code),
    FOREIGN KEY (company_id, store_id) REFERENCES core.stores(company_id, id),
    CHECK (store_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON core.store_settings FOR EACH ROW EXECUTE FUNCTION core.touch_row();
CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON core.store_settings
    FOR EACH ROW EXECUTE FUNCTION audit.log_row_change();

CREATE TABLE core.user_preferences (
    company_id      uuid NOT NULL REFERENCES platform.companies(id),
    user_id         uuid NOT NULL,
    setting_values  jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(setting_values) = 'object'),
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz,
    updated_by      uuid,
    row_ver         bigint NOT NULL DEFAULT 1,
    PRIMARY KEY (company_id, user_id),
    FOREIGN KEY (company_id, user_id) REFERENCES sec.users(company_id, id)
);
CREATE TRIGGER trg_touch BEFORE UPDATE ON core.user_preferences FOR EACH ROW EXECUTE FUNCTION core.touch_row();

SELECT core.enable_rls('core.company_settings'::regclass, false);
SELECT core.enable_rls('core.store_settings'::regclass, true);
SELECT core.enable_rls('core.user_preferences'::regclass, false);
CREATE POLICY own_preferences_only ON core.user_preferences AS RESTRICTIVE
    USING (user_id = (SELECT core.current_user_id()))
    WITH CHECK (user_id = (SELECT core.current_user_id()));

GRANT SELECT, INSERT, UPDATE ON core.company_settings, core.store_settings, core.user_preferences TO pos_app;

-- ---------------------------------------------------------------------
-- 2. Platform administrator actions: the vocabulary grows with the plan and status administration.
-- ---------------------------------------------------------------------
ALTER TABLE platform.admin_actions DROP CONSTRAINT admin_actions_action_check;
ALTER TABLE platform.admin_actions ADD CONSTRAINT admin_actions_action_check CHECK (action IN (
    'LOGIN', 'COMPANY_CREATED', 'STORE_CREATED', 'USER_CREATED',
    'PLAN_CREATED', 'PLAN_UPDATED', 'COMPANY_PLAN_ASSIGNED', 'COMPANY_STATUS_CHANGED'));

-- ---------------------------------------------------------------------
-- 3. Plans. Only the platform administration writes them, through functions (the application role no longer
--    has INSERT or UPDATE on platform.plans). A plan can be retired instead of deleted.
-- ---------------------------------------------------------------------
ALTER TABLE platform.plans ADD COLUMN is_active boolean NOT NULL DEFAULT true;
REVOKE INSERT, UPDATE ON platform.plans FROM pos_app;

CREATE FUNCTION platform.require_active_administrator(p_admin uuid) RETURNS void
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, public AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM platform.admins WHERE id = p_admin AND status = 'ACTIVE') THEN
        RAISE EXCEPTION 'platform administrator % is not active', p_admin USING ERRCODE = 'insufficient_privilege';
    END IF;
END $$;

CREATE FUNCTION platform.create_plan(p_admin uuid, p_code text, p_name text, p_max_stores int, p_max_users int,
                                     p_features jsonb) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
BEGIN
    PERFORM platform.require_active_administrator(p_admin);
    IF p_code IS NULL OR p_code !~ '^[A-Z][A-Z0-9_]{1,29}$' THEN
        RAISE EXCEPTION 'plan code: 2 to 30 capital letters, digits or underscores, starting with a letter' USING ERRCODE = 'check_violation';
    END IF;
    IF btrim(coalesce(p_name, '')) = '' THEN
        RAISE EXCEPTION 'plan name is required' USING ERRCODE = 'check_violation';
    END IF;
    INSERT INTO platform.plans (code, name, max_stores, max_users, features)
    VALUES (p_code, btrim(p_name), p_max_stores, p_max_users, COALESCE(p_features, '{}'::jsonb));
END $$;

CREATE FUNCTION platform.update_plan(p_admin uuid, p_code text, p_name text, p_max_stores int, p_max_users int,
                                     p_features jsonb, p_is_active boolean) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
BEGIN
    PERFORM platform.require_active_administrator(p_admin);
    IF btrim(coalesce(p_name, '')) = '' THEN
        RAISE EXCEPTION 'plan name is required' USING ERRCODE = 'check_violation';
    END IF;
    UPDATE platform.plans
       SET name = btrim(p_name), max_stores = p_max_stores, max_users = p_max_users,
           features = COALESCE(p_features, features), is_active = COALESCE(p_is_active, is_active)
     WHERE code = p_code;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'plan % does not exist', p_code USING ERRCODE = 'no_data_found';
    END IF;
END $$;

-- Assigns a plan to a company. A retired plan cannot be assigned. Existing usage above the new limits is allowed
-- (nothing is deleted); creating more stores or users is then refused by the limit triggers below.
CREATE FUNCTION platform.assign_company_plan(p_admin uuid, p_company uuid, p_plan_code text) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
BEGIN
    PERFORM platform.require_active_administrator(p_admin);
    IF NOT EXISTS (SELECT 1 FROM platform.plans WHERE code = p_plan_code AND is_active) THEN
        RAISE EXCEPTION 'plan % does not exist or is retired', p_plan_code USING ERRCODE = 'check_violation';
    END IF;
    UPDATE platform.companies SET plan_code = p_plan_code WHERE id = p_company;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'company % does not exist', p_company USING ERRCODE = 'no_data_found';
    END IF;
END $$;

-- ---------------------------------------------------------------------
-- 4. Company status. A suspended or closed company cannot sign in and its open sessions end at once.
--    The sign-in function sec.login is not changed: it inserts a session, and the trigger below refuses that insert.
-- ---------------------------------------------------------------------
CREATE FUNCTION platform.set_company_status(p_admin uuid, p_company uuid, p_status text) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_previous_company text := current_setting('app.company_id', true);
BEGIN
    PERFORM platform.require_active_administrator(p_admin);
    IF p_status NOT IN ('ACTIVE', 'SUSPENDED', 'CLOSED') THEN
        RAISE EXCEPTION 'status must be ACTIVE, SUSPENDED or CLOSED' USING ERRCODE = 'check_violation';
    END IF;
    UPDATE platform.companies SET status = p_status WHERE id = p_company;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'company % does not exist', p_company USING ERRCODE = 'no_data_found';
    END IF;
    IF p_status <> 'ACTIVE' THEN
        PERFORM set_config('app.company_id', p_company::text, true);   -- sec.sessions has forced row-level security
        UPDATE sec.sessions SET revoked_at = now(), revoke_reason = 'COMPANY_' || p_status
         WHERE company_id = p_company AND revoked_at IS NULL;
        PERFORM set_config('app.company_id', COALESCE(v_previous_company, ''), true);
    END IF;
END $$;

CREATE FUNCTION sec.refuse_sessions_for_inactive_company() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
BEGIN
    IF (SELECT status FROM platform.companies WHERE id = NEW.company_id) IS DISTINCT FROM 'ACTIVE' THEN
        RAISE EXCEPTION 'this company is suspended or closed; sign-in is not possible' USING ERRCODE = 'insufficient_privilege';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_company_must_be_active BEFORE INSERT ON sec.sessions
    FOR EACH ROW EXECUTE FUNCTION sec.refuse_sessions_for_inactive_company();

-- ---------------------------------------------------------------------
-- 5. Company profile. platform.companies is read-only for the application role, so the profile is changed here.
--    A company changes only its own row; a platform administrator (flag set by the API) may change any.
--    Only the listed fields can be patched. The change is written to audit.activity_log with old and new values.
-- ---------------------------------------------------------------------
CREATE FUNCTION platform.update_company_profile(p_company uuid, p_patch jsonb, p_expected_row_ver bigint DEFAULT NULL)
RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_allowed  text[] := ARRAY['name', 'legal_name', 'ntn', 'strn', 'timezone', 'currency_code', 'rounding_mode',
                               'price_band_basis', 'invoice_terms'];
    v_key      text;
    v_old      platform.companies%ROWTYPE;
    v_new      platform.companies%ROWTYPE;
    v_is_admin boolean := COALESCE(current_setting('app.platform_admin', true), '') = 'on';
BEGIN
    IF NOT (v_is_admin OR p_company = core.current_company_id()) THEN
        RAISE EXCEPTION 'a company can change only its own profile' USING ERRCODE = 'insufficient_privilege';
    END IF;
    FOR v_key IN SELECT jsonb_object_keys(p_patch) LOOP
        IF NOT v_key = ANY (v_allowed) THEN
            RAISE EXCEPTION 'company field % cannot be changed here', v_key USING ERRCODE = 'check_violation';
        END IF;
    END LOOP;

    SELECT * INTO v_old FROM platform.companies WHERE id = p_company FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'company % does not exist', p_company USING ERRCODE = 'no_data_found';
    END IF;
    IF p_expected_row_ver IS NOT NULL AND p_expected_row_ver <> v_old.row_ver THEN
        RAISE EXCEPTION 'the company profile was changed by someone else; reload and try again' USING ERRCODE = '40001';
    END IF;

    IF p_patch ? 'timezone' AND NOT EXISTS (SELECT 1 FROM pg_timezone_names WHERE name = p_patch->>'timezone') THEN
        RAISE EXCEPTION 'unknown time zone %', p_patch->>'timezone' USING ERRCODE = 'check_violation';
    END IF;
    IF p_patch ? 'currency_code' AND (p_patch->>'currency_code') !~ '^[A-Z]{3}$' THEN
        RAISE EXCEPTION 'currency code must be three capital letters' USING ERRCODE = 'check_violation';
    END IF;
    IF p_patch ? 'name' AND btrim(coalesce(p_patch->>'name', '')) = '' THEN
        RAISE EXCEPTION 'business name is required' USING ERRCODE = 'check_violation';
    END IF;

    UPDATE platform.companies SET
        name             = CASE WHEN p_patch ? 'name' THEN btrim(p_patch->>'name') ELSE name END,
        legal_name       = CASE WHEN p_patch ? 'legal_name' THEN NULLIF(btrim(p_patch->>'legal_name'), '') ELSE legal_name END,
        ntn              = CASE WHEN p_patch ? 'ntn' THEN NULLIF(btrim(p_patch->>'ntn'), '') ELSE ntn END,
        strn             = CASE WHEN p_patch ? 'strn' THEN NULLIF(btrim(p_patch->>'strn'), '') ELSE strn END,
        timezone         = CASE WHEN p_patch ? 'timezone' THEN p_patch->>'timezone' ELSE timezone END,
        currency_code    = CASE WHEN p_patch ? 'currency_code' THEN p_patch->>'currency_code' ELSE currency_code END,
        rounding_mode    = CASE WHEN p_patch ? 'rounding_mode' THEN p_patch->>'rounding_mode' ELSE rounding_mode END,
        price_band_basis = CASE WHEN p_patch ? 'price_band_basis' THEN p_patch->>'price_band_basis' ELSE price_band_basis END,
        invoice_terms    = CASE WHEN p_patch ? 'invoice_terms' THEN NULLIF(btrim(p_patch->>'invoice_terms'), '') ELSE invoice_terms END
     WHERE id = p_company
    RETURNING * INTO v_new;

    INSERT INTO audit.activity_log (company_id, user_id, action, entity_type, entity_id, old_values, new_values)
    VALUES (p_company, core.current_user_id(), 'UPDATE', 'platform.companies', p_company::text,
            to_jsonb(v_old) - 'settings', to_jsonb(v_new) - 'settings');
    RETURN v_new.row_ver;
END $$;

-- ---------------------------------------------------------------------
-- 6. Plan limits, enforced by the database whoever writes the row (decision: limits are the plan's max_stores / max_users;
--    the head office is not counted; a deleted user or an inactive store is not counted).
-- ---------------------------------------------------------------------
CREATE FUNCTION core.enforce_store_limit() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_limit int;
    v_count int;
BEGIN
    IF NEW.is_head_office OR NOT NEW.is_active THEN RETURN NEW; END IF;
    SELECT p.max_stores INTO v_limit
      FROM platform.companies c JOIN platform.plans p ON p.code = c.plan_code WHERE c.id = NEW.company_id;
    SELECT count(*) INTO v_count FROM core.stores s
     WHERE s.company_id = NEW.company_id AND NOT s.is_head_office AND s.is_active
       AND (TG_OP = 'INSERT' OR s.id <> NEW.id);
    IF v_limit IS NOT NULL AND v_count >= v_limit THEN
        RAISE EXCEPTION 'the plan allows at most % active stores', v_limit USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_store_plan_limit BEFORE INSERT OR UPDATE OF is_active ON core.stores
    FOR EACH ROW EXECUTE FUNCTION core.enforce_store_limit();

CREATE FUNCTION sec.enforce_user_limit() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_limit int;
    v_count int;
BEGIN
    SELECT p.max_users INTO v_limit
      FROM platform.companies c JOIN platform.plans p ON p.code = c.plan_code WHERE c.id = NEW.company_id;
    SELECT count(*) INTO v_count FROM sec.users u WHERE u.company_id = NEW.company_id AND NOT u.is_deleted;
    IF v_limit IS NOT NULL AND v_count >= v_limit THEN
        RAISE EXCEPTION 'the plan allows at most % users', v_limit USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_user_plan_limit BEFORE INSERT ON sec.users
    FOR EACH ROW EXECUTE FUNCTION sec.enforce_user_limit();

-- ---------------------------------------------------------------------
-- 7. Backup status. ops.* is closed to the application role; these read-only functions expose only what the screens show.
--    Backups run outside the database (operator commands); a company sees its own export history, never storage locations.
-- ---------------------------------------------------------------------
CREATE FUNCTION ops.read_backup_health() RETURNS SETOF ops.v_backup_health
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, public AS $$
    SELECT * FROM ops.v_backup_health
$$;

CREATE FUNCTION ops.list_backup_runs(p_limit int DEFAULT 50)
RETURNS TABLE (out_id uuid, out_kind text, out_company_id uuid, out_started_at timestamptz, out_finished_at timestamptz,
               out_status text, out_storage_location text, out_size_bytes bigint, out_encrypted boolean, out_error_message text)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, public AS $$
BEGIN
    IF COALESCE(current_setting('app.platform_admin', true), '') <> 'on' THEN
        RAISE EXCEPTION 'only platform administrators may read backup runs' USING ERRCODE = 'insufficient_privilege';
    END IF;
    RETURN QUERY
        SELECT r.id, r.kind, r.company_id, r.started_at, r.finished_at, r.status, r.storage_location, r.size_bytes,
               r.encrypted, r.error_message
          FROM ops.backup_runs r
         ORDER BY r.started_at DESC
         LIMIT least(greatest(COALESCE(p_limit, 50), 1), 200);
END $$;

CREATE FUNCTION ops.list_company_exports(p_limit int DEFAULT 50)
RETURNS TABLE (out_id uuid, out_started_at timestamptz, out_finished_at timestamptz, out_status text,
               out_size_bytes bigint, out_encrypted boolean)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, public AS $$
    SELECT r.id, r.started_at, r.finished_at, r.status, r.size_bytes, r.encrypted
      FROM ops.backup_runs r
     WHERE r.kind = 'LOGICAL_EXPORT' AND r.company_id = core.current_company_id()
     ORDER BY r.started_at DESC
     LIMIT least(greatest(COALESCE(p_limit, 50), 1), 200)
$$;

CREATE FUNCTION ops.list_restore_drills(p_limit int DEFAULT 50)
RETURNS TABLE (out_id uuid, out_started_at timestamptz, out_finished_at timestamptz, out_outcome text,
               out_measured_rto_minutes numeric, out_measured_rpo_minutes numeric, out_rows_checked bigint)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, public AS $$
    SELECT d.id, d.started_at, d.finished_at, d.outcome, d.measured_rto_minutes, d.measured_rpo_minutes, d.rows_checked
      FROM ops.restore_drills d
     ORDER BY d.finished_at DESC
     LIMIT least(greatest(COALESCE(p_limit, 50), 1), 200)
$$;

-- ---------------------------------------------------------------------
-- 8. Grants: functions are callable by the API role only.
-- ---------------------------------------------------------------------
REVOKE ALL ON FUNCTION platform.require_active_administrator(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION platform.create_plan(uuid, text, text, int, int, jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION platform.update_plan(uuid, text, text, int, int, jsonb, boolean) FROM PUBLIC;
REVOKE ALL ON FUNCTION platform.assign_company_plan(uuid, uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION platform.set_company_status(uuid, uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION platform.update_company_profile(uuid, jsonb, bigint) FROM PUBLIC;
REVOKE ALL ON FUNCTION ops.read_backup_health() FROM PUBLIC;
REVOKE ALL ON FUNCTION ops.list_backup_runs(int) FROM PUBLIC;
REVOKE ALL ON FUNCTION ops.list_company_exports(int) FROM PUBLIC;
REVOKE ALL ON FUNCTION ops.list_restore_drills(int) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION platform.create_plan(uuid, text, text, int, int, jsonb) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.update_plan(uuid, text, text, int, int, jsonb, boolean) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.assign_company_plan(uuid, uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.set_company_status(uuid, uuid, text) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.update_company_profile(uuid, jsonb, bigint) TO pos_app;
GRANT EXECUTE ON FUNCTION ops.read_backup_health() TO pos_app;
GRANT EXECUTE ON FUNCTION ops.list_backup_runs(int) TO pos_app;
GRANT EXECUTE ON FUNCTION ops.list_company_exports(int) TO pos_app;
GRANT EXECUTE ON FUNCTION ops.list_restore_drills(int) TO pos_app;
