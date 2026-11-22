-- Rolls back 0210.
REVOKE DELETE ON master.sizes FROM pos_app;
DROP FUNCTION IF EXISTS core.find_row_usages(uuid, regclass, uuid);
