-- Rolls back 0001. The synchronizer needs the columns, so apply 0001 again before starting the API.
DROP INDEX IF EXISTS sec.ix_permissions_tree;

ALTER TABLE sec.permissions
    DROP COLUMN IF EXISTS sort_order,
    DROP COLUMN IF EXISTS action_code,
    DROP COLUMN IF EXISTS screen_code,
    DROP COLUMN IF EXISTS module_code;
