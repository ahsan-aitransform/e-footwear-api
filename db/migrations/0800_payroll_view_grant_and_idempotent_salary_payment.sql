-- 0800: Staff and payroll (docx 3.8, 3.9; Part 16).
--   * the application role could not read hr.v_salary_outstanding (the baseline grants only two views after the tables exist): it is
--     the source of the Salary Outstanding report;
--   * paying a salary must be safe to retry (Part 26: a retried request never creates a second payment or journal entry). The baseline
--     function hr.pay_salary has no client transaction id and hr.salary_payments is append-only (no UPDATE), so the id is kept in a small
--     table of its own and hr.pay_salary_once wraps hr.pay_salary: a repeated client transaction id returns the first payment.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

GRANT SELECT ON hr.v_salary_outstanding TO pos_app;

CREATE TABLE hr.salary_payment_client_ids (
    company_id            uuid NOT NULL REFERENCES platform.companies(id),
    client_transaction_id uuid NOT NULL,
    salary_payment_id     uuid NOT NULL,
    created_at            timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, client_transaction_id),
    FOREIGN KEY (company_id, salary_payment_id) REFERENCES hr.salary_payments(company_id, id)
);
SELECT core.enable_rls('hr.salary_payment_client_ids'::regclass, false);
GRANT SELECT, INSERT ON hr.salary_payment_client_ids TO pos_app;

CREATE FUNCTION hr.pay_salary_once(p_company uuid, p_line uuid, p_amount numeric, p_method text, p_paid_on date,
                                   p_user uuid, p_reference text, p_client_transaction uuid)
RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
    v_payment uuid;
BEGIN
    IF p_client_transaction IS NOT NULL THEN
        SELECT c.salary_payment_id INTO v_payment FROM hr.salary_payment_client_ids c
         WHERE c.company_id = p_company AND c.client_transaction_id = p_client_transaction;
        IF FOUND THEN RETURN v_payment; END IF;
    END IF;

    v_payment := hr.pay_salary(p_company, p_line, p_amount, p_method, p_paid_on, p_user, p_reference);

    IF p_client_transaction IS NOT NULL THEN
        INSERT INTO hr.salary_payment_client_ids (company_id, client_transaction_id, salary_payment_id)
        VALUES (p_company, p_client_transaction, v_payment);
    END IF;
    RETURN v_payment;
EXCEPTION
    WHEN unique_violation THEN
        -- A concurrent request with the same client transaction id won the race: this work is rolled back, return its payment.
        IF p_client_transaction IS NOT NULL THEN
            SELECT c.salary_payment_id INTO v_payment FROM hr.salary_payment_client_ids c
             WHERE c.company_id = p_company AND c.client_transaction_id = p_client_transaction;
            IF FOUND THEN RETURN v_payment; END IF;
        END IF;
        RAISE;
END $$;

GRANT EXECUTE ON FUNCTION hr.pay_salary_once(uuid, uuid, numeric, text, date, uuid, text, uuid) TO pos_app;
