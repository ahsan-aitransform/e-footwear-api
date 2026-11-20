-- DEV ONLY. Adds two users to the existing company "Test Footwear Co" (company id 1).
-- Safe to run more than once: users that already exist are skipped.
-- Run as the postgres admin (it must be able to write before any login exists):
--
--   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -U postgres -d E-Footwear -f db/dev_seed.sql
--
-- Logins created (both use the password  Owner@12345 ):
--   company 1 / owner    - COMPANY_OWNER, sees every store
--   company 1 / cashier  - CASHIER_SALESMAN, sees Store 1 only
-- Change these passwords before using this database for anything real.

BEGIN;

INSERT INTO sec.users (company_id, username, email, password_hash, full_name, role_code, all_stores, must_change_password)
SELECT 1, 'owner', 'owner.c1@example.test', '$argon2id$v=19$m=19456,t=2,p=1$tgvjL0a42osSVL7bOfw48Q==$DMQaZ0JVWTCa2t19xZonmDStXPwNizT7DoL86Cly79k=', 'Dev Owner', 'COMPANY_OWNER', true, false
WHERE NOT EXISTS (SELECT 1 FROM sec.users WHERE company_id = 1 AND lower(username) = 'owner');

INSERT INTO sec.users (company_id, username, email, password_hash, full_name, role_code, all_stores, must_change_password)
SELECT 1, 'cashier', 'cashier.c1@example.test', '$argon2id$v=19$m=19456,t=2,p=1$tgvjL0a42osSVL7bOfw48Q==$DMQaZ0JVWTCa2t19xZonmDStXPwNizT7DoL86Cly79k=', 'Dev Cashier', 'CASHIER_SALESMAN', false, false
WHERE NOT EXISTS (SELECT 1 FROM sec.users WHERE company_id = 1 AND lower(username) = 'cashier');

-- the cashier works in Store 1 only
INSERT INTO sec.user_stores (company_id, user_id, store_id)
SELECT 1, u.id, s.id
  FROM sec.users u, core.stores s
 WHERE u.company_id = 1 AND u.username = 'cashier' AND s.company_id = 1 AND s.code = 'S1'
ON CONFLICT DO NOTHING;

COMMIT;

SELECT username, role_code, all_stores FROM sec.users WHERE company_id = 1 ORDER BY id;
