-- 0005: every company user has an email. The email is the sign-in name and is unique across ALL companies,
-- so it alone identifies the person, their company and their role (no company field at sign-in).
--   psql -h localhost -U postgres -d E-Footwear -v ON_ERROR_STOP=1 -f db/migrations/0005_user_email.sql
BEGIN;

ALTER TABLE sec.users ADD COLUMN email text;

-- Existing dev users get a placeholder address built from their username. Change them if you want real ones.
UPDATE sec.users SET email = lower(username) || '.c' || company_id || '@example.test' WHERE email IS NULL;

ALTER TABLE sec.users ALTER COLUMN email SET NOT NULL;
ALTER TABLE sec.users ADD CONSTRAINT users_email_format_chk CHECK (email ~ '^[^@\s]+@[^@\s]+\.[^@\s]+$');
CREATE UNIQUE INDEX ux_users_email ON sec.users (lower(email)) WHERE NOT is_deleted;

-- Before sign-in no company is selected, so row-level security hides every user. These two functions run as the
-- owner and reveal only what sign-in and user creation need: a company id, and whether an email is taken.
CREATE FUNCTION platform.find_company_by_user_email(p_email text) RETURNS bigint
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
DROP FUNCTION platform.create_company(bigint, text, text, text, text, text, text, text);
CREATE FUNCTION platform.create_company(p_admin_id bigint, p_name text, p_timezone text,
                                        p_owner_email text, p_owner_full_name text, p_owner_hash text,
                                        p_store_code text, p_store_name text)
RETURNS TABLE (out_company_id bigint, out_store_id bigint, out_owner_id bigint)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_company bigint;
    v_store   bigint;
    v_owner   bigint;
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

REVOKE ALL ON FUNCTION platform.create_company(bigint, text, text, text, text, text, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION platform.create_company(bigint, text, text, text, text, text, text, text) TO pos_app;

COMMIT;
