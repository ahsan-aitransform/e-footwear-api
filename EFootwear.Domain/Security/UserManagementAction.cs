namespace EFootwear.Domain.Security;

/// <summary>What a person wants to do to another user's account. Used by <see cref="UserManagementRules"/>.</summary>
public enum UserManagementAction
{
    View,
    EditProfile,
    ChangeRoleOrStores,
    ChangeStatus,
    ResetPassword,
    Delete,
    ChangePermissions,
    ManageSessions,
}
