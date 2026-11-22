-- Rolls back 0700. Vouchers posted through acct.post_voucher keep their rows; only the helpers, the policies, the idempotency
-- column and the corrected view are removed. Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.
DROP FUNCTION IF EXISTS acct.void_voucher(uuid, uuid, text);
DROP FUNCTION IF EXISTS acct.post_voucher(uuid, uuid, text, date, uuid, uuid, uuid, uuid, core.money, text, text, text, uuid, jsonb);
DROP FUNCTION IF EXISTS acct.resolve_voucher_side(uuid, uuid, uuid, text);

-- the baseline view (counts every allocation, void vouchers included)
CREATE OR REPLACE VIEW purch.v_invoice_balance WITH (security_invoker = true) AS
SELECT i.company_id, i.id AS invoice_id, i.store_id, i.vendor_party_id, i.bill_date, i.due_date,
       i.net_amount, COALESCE(a.paid, 0) AS paid_amount,
       i.net_amount - COALESCE(a.paid, 0) AS remaining_amount
  FROM purch.invoices i
  LEFT JOIN (SELECT company_id, purchase_invoice_id, SUM(amount) AS paid
               FROM acct.voucher_allocations WHERE purchase_invoice_id IS NOT NULL
              GROUP BY company_id, purchase_invoice_id) a
         ON a.company_id = i.company_id AND a.purchase_invoice_id = i.id
 WHERE i.doc_type = 'PURCHASE' AND i.status = 'POSTED';

REVOKE SELECT ON purch.v_invoice_balance FROM pos_app;

DROP POLICY IF EXISTS store_isolation ON acct.voucher_allocations;
DROP POLICY IF EXISTS store_isolation ON acct.vouchers;
DROP INDEX IF EXISTS acct.ux_vouchers_client_transaction;
ALTER TABLE acct.vouchers DROP COLUMN IF EXISTS client_transaction_id;
