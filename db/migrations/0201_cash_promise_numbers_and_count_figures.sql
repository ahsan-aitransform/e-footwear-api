-- 0201: cash screens of the Party chapter (Phase 0 rows ORG-3.5 Promise, ORG-3.6 Cash Calculator).
--   * cash.promises.promise_no: docx 3.5 "Promise Id (Read-only; auto number)". The number is allocated per store by core.next_number.
--   * cash.cash_counts.payments_amount / credit_sales_amount / credit_received_amount: docx 3.6 lists "Payments", "Credit" and
--     "Credit Received" next to the denomination counts. They are figures typed in by the person counting (as in the old system).
--     "Net Sale" is NOT stored: its formula is not documented ([BUSINESS DECISION REQUIRED], docx 3.6 says "confirm with test values").
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

ALTER TABLE cash.promises
    ADD COLUMN promise_no bigint;
CREATE UNIQUE INDEX ux_promises_number ON cash.promises (company_id, store_id, promise_no) WHERE promise_no IS NOT NULL;

ALTER TABLE cash.cash_counts
    ADD COLUMN payments_amount        core.money CHECK (payments_amount IS NULL OR payments_amount >= 0),
    ADD COLUMN credit_sales_amount    core.money CHECK (credit_sales_amount IS NULL OR credit_sales_amount >= 0),
    ADD COLUMN credit_received_amount core.money CHECK (credit_received_amount IS NULL OR credit_received_amount >= 0);

-- row_ver never increased on cash_counts (the baseline gave only cash.promises its touch trigger), so two people saving the
-- same day's count could not be told apart. This adds the shared trigger that also keeps updated_at / updated_by.
CREATE TRIGGER trg_touch BEFORE UPDATE ON cash.cash_counts FOR EACH ROW EXECUTE FUNCTION core.touch_row();
