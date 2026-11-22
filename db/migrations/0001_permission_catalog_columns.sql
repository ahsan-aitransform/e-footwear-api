-- 0001: describe each permission by module / screen / action, so the permission tree can be stored with the data and ordered.
-- The rows are filled at API startup by PermissionCatalogSynchronizer from the in-code catalog
-- (EFootwear.Domain/Security/Permissions/PermissionCatalogDefinitions.cs). The columns are nullable: rows created by older
-- scripts (the legacy codes such as SALE_CREATE) are described by the synchronizer too, but must stay valid without it.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

ALTER TABLE sec.permissions
    ADD COLUMN IF NOT EXISTS module_code text,
    ADD COLUMN IF NOT EXISTS screen_code text,
    ADD COLUMN IF NOT EXISTS action_code text,
    ADD COLUMN IF NOT EXISTS sort_order  integer;

CREATE INDEX IF NOT EXISTS ix_permissions_tree ON sec.permissions (module_code, screen_code, sort_order);
