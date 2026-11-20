-- 0002: closes gaps in the v6 baseline found while building the API. The baseline file itself is left exactly as supplied.
--
-- platform.companies and platform.plans are not in the baseline's RLS list, yet pos_app may INSERT/UPDATE them, so a tenant
-- session could read or change another company's profile (rounding_mode, plan, status ...) or create companies.
-- Companies are registered by the operator CLI with the owner connection, so pos_app never needs INSERT/DELETE here.
--  * companies: every session may read (the sign-in screen looks a company up by name or id before any scope exists);
--               a session may update only its own company.
--  * plans: read only.
BEGIN;

REVOKE INSERT, DELETE ON platform.companies FROM pos_app;
REVOKE INSERT, UPDATE, DELETE ON platform.plans FROM pos_app;

ALTER TABLE platform.companies ENABLE ROW LEVEL SECURITY;
CREATE POLICY companies_read ON platform.companies FOR SELECT USING (true);
CREATE POLICY companies_update_own ON platform.companies FOR UPDATE
    USING (id = (SELECT core.current_company_id()))
    WITH CHECK (id = (SELECT core.current_company_id()));

-- The baseline grants DELETE on a short list only, so removing a user's store or permission (an edit of the user) is impossible.
-- These three are edited by replacing their rows; they are not ledgers.
GRANT DELETE ON sec.user_stores, sec.user_permissions, master.sizes TO pos_app;

COMMIT;

-- promo.rebuild_effective_prices (not SECURITY DEFINER) deletes stale price rows and re-derives conflict candidates; the
-- baseline never granted pos_app DELETE on them, so the rebuild would fail for every caller.
BEGIN;
GRANT DELETE ON promo.effective_prices, promo.conflict_candidates TO pos_app;
COMMIT;

BEGIN;
GRANT DELETE ON catalog.store_price_overrides TO pos_app;   -- clearing a per-store price override
COMMIT;

BEGIN;
-- voiding a voucher removes its settlement rows (the voucher itself stays, marked VOID, with a reversing journal entry); plan membership is replaced as a whole
GRANT DELETE ON acct.voucher_allocations, hr.commission_plan_members TO pos_app;
COMMIT;

BEGIN;
-- The baseline grants SELECT on "ALL TABLES" before it creates its reporting views, so pos_app cannot read any view (the views are security_invoker:
-- row-level security of the caller still applies). ops.v_backup_health is not security_invoker and is left alone.
GRANT SELECT ON acct.v_account_balance, purch.v_invoice_balance, purch.v_vendor_summary, inv.v_transfer_loss_board, exp.v_expense_store_month, hr.v_commission_summary,
                hr.v_salary_outstanding, exp.v_expense_approval_queue, purch.v_vendor_order_lines, purch.v_vendor_due, rpt.v_non_moving_stock, rpt.v_transfer_opportunities TO pos_app;
COMMIT;
