-- Rolls back 1506_remove_unused_invoice_buyer_tax_columns. The columns come back empty (they never held data).
ALTER TABLE sales.invoices
    ADD COLUMN buyer_ntn text,
    ADD COLUMN buyer_cnic text;
