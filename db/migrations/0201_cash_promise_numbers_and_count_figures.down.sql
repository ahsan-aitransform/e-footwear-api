-- Rolls back 0201.
DROP TRIGGER IF EXISTS trg_touch ON cash.cash_counts;
ALTER TABLE cash.cash_counts
    DROP COLUMN IF EXISTS credit_received_amount,
    DROP COLUMN IF EXISTS credit_sales_amount,
    DROP COLUMN IF EXISTS payments_amount;

DROP INDEX IF EXISTS cash.ux_promises_number;
ALTER TABLE cash.promises DROP COLUMN IF EXISTS promise_no;
