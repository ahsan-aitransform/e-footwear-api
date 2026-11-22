-- 0101: POS device authorization (Part 6 device authorization, Part 23.7).
-- A device is registered first (inactive, never authorized = pending), a person with the approve permission authorizes it, and an
-- authorized device can be revoked. The columns keep who and when; the full history is in the activity log (trigger below).
-- The touch trigger makes row_ver work for devices, so two people editing one device cannot silently overwrite each other.
-- Do not add BEGIN / COMMIT: the migration runner wraps this script in a transaction.

ALTER TABLE core.devices
    ADD COLUMN authorized_at timestamptz,
    ADD COLUMN authorized_by uuid,
    ADD COLUMN revoked_at    timestamptz,
    ADD COLUMN revoked_by    uuid,
    ADD COLUMN revoke_reason text CHECK (revoke_reason IS NULL OR length(revoke_reason) <= 200);

ALTER TABLE core.devices
    ADD CONSTRAINT devices_revoked_is_inactive_chk CHECK (revoked_at IS NULL OR NOT is_active);

CREATE TRIGGER trg_touch BEFORE UPDATE ON core.devices FOR EACH ROW EXECUTE FUNCTION core.touch_row();
CREATE TRIGGER trg_audit AFTER INSERT OR UPDATE OR DELETE ON core.devices
    FOR EACH ROW EXECUTE FUNCTION audit.log_row_change();
