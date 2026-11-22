-- 0002: core.stores and platform.companies have updated_at / updated_by / row_ver columns but, unlike the other mutable tables,
-- no trg_touch trigger, so row_ver never changed and optimistic concurrency (EF Core row_ver token) could not detect a lost update.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

CREATE TRIGGER trg_touch BEFORE UPDATE ON core.stores
    FOR EACH ROW EXECUTE FUNCTION core.touch_row();

CREATE TRIGGER trg_touch BEFORE UPDATE ON platform.companies
    FOR EACH ROW EXECUTE FUNCTION core.touch_row();
