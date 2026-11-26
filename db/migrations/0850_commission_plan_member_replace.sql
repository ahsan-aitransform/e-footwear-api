-- 0850: Commissions (Part 16.2).
--   * the members of a commission plan are replaced as one set when the plan is saved (one person, a group, or nobody = the whole role). The baseline
--     grants the application role only SELECT, INSERT and UPDATE on hr.commission_plan_members, so it could add a member but never remove one.
--     The members table carries only the plan and the staff member (no history of its own): every change is already written to the audit trail by the
--     trigger trg_audit, so deleting a row loses nothing that is not recorded.
-- Commission entries, rules and plans keep their baseline grants: entries are never deleted, a rule is ended (valid_to) and a plan is marked deleted.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

GRANT DELETE ON hr.commission_plan_members TO pos_app;
