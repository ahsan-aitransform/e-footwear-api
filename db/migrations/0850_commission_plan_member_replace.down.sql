-- Down of 0850: the application role can no longer remove members of a commission plan.
REVOKE DELETE ON hr.commission_plan_members FROM pos_app;
