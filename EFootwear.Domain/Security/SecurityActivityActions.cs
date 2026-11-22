namespace EFootwear.Domain.Security;

/// <summary>
/// Action names the security module writes to the activity log (audit.activity_log) for events the row-change trigger cannot describe
/// on its own: a whole permission set changing, sessions being revoked, a user being deleted. They appear in the Users Activity screen.
/// </summary>
public static class SecurityActivityActions
{
    public const string PermissionsChanged = "PERMISSIONS_CHANGED";
    public const string StoreAssignmentsChanged = "STORE_ASSIGNMENTS_CHANGED";
    public const string QuickActionsChanged = "QUICK_ACTIONS_CHANGED";
    public const string UserDeleted = "USER_DELETED";
    public const string UserPasswordResetByAdministrator = "USER_PASSWORD_RESET_BY_ADMINISTRATOR";
    public const string PasswordResetTokenIssued = "PASSWORD_RESET_TOKEN_ISSUED";
    public const string SessionRevoked = "SESSION_REVOKED";
    public const string DeviceRegistered = "DEVICE_REGISTERED";
    public const string DeviceTokenRotated = "DEVICE_TOKEN_ROTATED";

    public const string UserEntityType = "sec.users";
    public const string UserPermissionsEntityType = "sec.user_permissions";
    public const string UserStoresEntityType = "sec.user_stores";
    public const string UserQuickActionsEntityType = "sec.user_quick_actions";
    public const string UserSessionsEntityType = "sec.sessions";
    public const string DeviceEntityType = "core.devices";
}
