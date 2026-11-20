-- Run once per database, as the schema owner (postgres), AFTER the schema file.
-- The API must connect as this role, NOT as postgres: a superuser bypasses row-level security,
-- which switches off all tenant isolation. The API refuses to start if it is given such a role.
--
--   psql -h localhost -U postgres -d E-Footwear -v pw="'choose-a-strong-password'" -f create_app_login.sql

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pos_app_login') THEN
        CREATE ROLE pos_app_login LOGIN NOBYPASSRLS NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
END $$;

GRANT pos_app TO pos_app_login;
ALTER ROLE pos_app_login PASSWORD :pw;

-- the login role may only connect to this database
SELECT format('GRANT CONNECT ON DATABASE %I TO pos_app_login', current_database()) \gexec
