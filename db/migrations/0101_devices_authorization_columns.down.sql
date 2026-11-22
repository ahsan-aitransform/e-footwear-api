DROP TRIGGER IF EXISTS trg_audit ON core.devices;
DROP TRIGGER IF EXISTS trg_touch ON core.devices;
ALTER TABLE core.devices DROP CONSTRAINT IF EXISTS devices_revoked_is_inactive_chk;
ALTER TABLE core.devices
    DROP COLUMN IF EXISTS authorized_at,
    DROP COLUMN IF EXISTS authorized_by,
    DROP COLUMN IF EXISTS revoked_at,
    DROP COLUMN IF EXISTS revoked_by,
    DROP COLUMN IF EXISTS revoke_reason;
