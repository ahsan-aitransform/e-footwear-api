-- 1506_remove_unused_invoice_buyer_tax_columns
-- sales.invoices carried buyer_ntn and buyer_cnic ("tax-ready fields") that nothing in the application ever wrote or read. They are removed.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.
ALTER TABLE sales.invoices
    DROP COLUMN buyer_ntn,
    DROP COLUMN buyer_cnic;
