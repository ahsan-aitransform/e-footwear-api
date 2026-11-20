-- 0003: platform administrators, company creation through the platform, and user-creation support.
-- Apply once, as the database owner (postgres), after the v6 schema:
--   psql -h localhost -U postgres -d E-Footwear -v ON_ERROR_STOP=1 -f db/migrations/0003_platform_admin_and_users.sql
BEGIN;

-- ---------------------------------------------------------------------
-- 1. Platform administrators. They are NOT company users: they have no company, and they
--    sign in to a separate token space ("admin.<secret>"). Created by the operator CLI only.
-- ---------------------------------------------------------------------
CREATE TABLE platform.admins (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    admin_id        bigint NOT NULL REFERENCES platform.admins(id),
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
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    admin_id        bigint NOT NULL REFERENCES platform.admins(id),
    action          text NOT NULL CHECK (action IN ('LOGIN','COMPANY_CREATED','STORE_CREATED','USER_CREATED')),
    company_id      bigint REFERENCES platform.companies(id),
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
RETURNS TABLE (out_outcome text, out_admin_id bigint, out_session_token text, out_locked_until timestamptz)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    a       platform.admins%ROWTYPE;
    v_token text;
    v_fails int;
BEGIN
    SELECT * INTO a FROM platform.admins WHERE lower(username) = lower(p_username) FOR UPDATE;
    IF NOT FOUND OR a.status <> 'ACTIVE' THEN
        RETURN QUERY SELECT 'INVALID'::text, NULL::bigint, NULL::text, NULL::timestamptz;
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
        RETURN QUERY SELECT 'INVALID'::text, NULL::bigint, NULL::text, NULL::timestamptz;
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
RETURNS TABLE (out_admin_id bigint, out_username text, out_full_name text)
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
CREATE FUNCTION platform.create_company(p_admin_id bigint, p_name text, p_timezone text,
                                        p_owner_username text, p_owner_full_name text, p_owner_hash text,
                                        p_store_code text, p_store_name text)
RETURNS TABLE (out_company_id bigint, out_store_id bigint, out_owner_id bigint)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_company bigint;
    v_store   bigint;
    v_owner   bigint;
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
REVOKE ALL ON FUNCTION platform.create_company(bigint, text, text, text, text, text, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION platform.admin_login(text, boolean, inet, text) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.admin_session(text) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.admin_logout(text) TO pos_app;
GRANT EXECUTE ON FUNCTION platform.create_company(bigint, text, text, text, text, text, text, text) TO pos_app;

COMMIT;
