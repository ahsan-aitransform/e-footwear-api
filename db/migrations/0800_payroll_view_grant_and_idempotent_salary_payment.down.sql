-- Down for 0800. Payments already recorded stay (hr.salary_payments is append-only); only the retry protection and the grant go.
DROP FUNCTION IF EXISTS hr.pay_salary_once(uuid, uuid, numeric, text, date, uuid, text, uuid);
DROP TABLE IF EXISTS hr.salary_payment_client_ids;
REVOKE SELECT ON hr.v_salary_outstanding FROM pos_app;
