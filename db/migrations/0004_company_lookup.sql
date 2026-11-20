-- 0004: lets sign-in accept a company NAME as well as an id.
-- Before a user is signed in, no company is selected, so row-level security would hide every company row.
-- This function runs as the owner and returns only an id, and only when exactly one company has that name.
-- Two companies with the same name return nothing (sign-in by id still works), so a name never guesses.
--   psql -h localhost -U postgres -d E-Footwear -v ON_ERROR_STOP=1 -f db/migrations/0004_company_lookup.sql
BEGIN;

CREATE FUNCTION platform.find_company_by_name(p_name text) RETURNS bigint
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, public AS $$
    SELECT CASE WHEN count(*) = 1 THEN min(id) END
      FROM platform.companies
     WHERE lower(name) = lower(btrim(p_name))
$$;

REVOKE ALL ON FUNCTION platform.find_company_by_name(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION platform.find_company_by_name(text) TO pos_app;

COMMIT;
