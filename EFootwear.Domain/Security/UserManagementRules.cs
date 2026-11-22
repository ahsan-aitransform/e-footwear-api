using EFootwear.Domain.Common;

namespace EFootwear.Domain.Security;

/// <summary>
/// Who may do what to ANOTHER user's account. It extends <see cref="UserCreationRules"/>: you may manage exactly the roles
/// you may create, and only inside the stores you work in. Pure code, unit-tested without a database.
///
///   * The company owner account is protected (docx 1.3: "ADMIN cannot be deleted"): nobody else may edit, block, reset,
///     delete or change its permissions. The owner manages its own profile through Change Password and its own sessions.
///   * Nobody may block, delete or change the role or permissions of their own account (a manager could otherwise lock
///     themselves out or raise their own rights). Own password: Change Password. Own profile name and email are allowed.
///   * A store manager reaches only users who work in at least one of its stores.
/// Platform administrators do not use these endpoints; they are refused earlier by the CompanyUserOnly policy.
/// </summary>
public static class UserManagementRules
{
    public static void EnsureCanManage(Caller caller, Guid callerUserId, ManagedUserSummary target, UserManagementAction action)
    {
        var isOwnAccount = target.UserId == callerUserId;

        if (isOwnAccount)
        {
            EnsureOwnAccountActionIsAllowed(action);
            return;
        }

        if (target.RoleCode == RoleCodes.CompanyOwner)
            throw new DomainException(ErrorKind.Forbidden, "The company owner account cannot be changed by other users.");

        if (!UserCreationRules.CreatableRoles(caller).Contains(target.RoleCode))
            throw new DomainException(ErrorKind.Forbidden, $"You are not allowed to manage a user with role {target.RoleCode}.");

        if (!caller.HasAccessToAllStores && !target.StoreIds.Any(caller.AssignedStoreIds.Contains))
            throw new DomainException(ErrorKind.Forbidden, "That user does not work in any of your stores.");
    }

    private static void EnsureOwnAccountActionIsAllowed(UserManagementAction action)
    {
        switch (action)
        {
            case UserManagementAction.View:
            case UserManagementAction.EditProfile:
            case UserManagementAction.ManageSessions:
                return;
            case UserManagementAction.ResetPassword:
                throw new DomainException(ErrorKind.Forbidden, "Use Change Password for your own account.");
            default:
                throw new DomainException(ErrorKind.Forbidden, "You cannot do that to your own account.");
        }
    }

    /// <summary>
    /// The store assignment after an edit. A caller who does not see every store can only change the assignments inside their own
    /// stores: assignments outside them stay as they are (they are not the caller's to remove).
    /// </summary>
    public static IReadOnlyList<Guid> MergeStoreAssignments(Caller caller, IReadOnlyCollection<Guid> currentStoreIds,
                                                            IReadOnlyCollection<Guid> requestedStoreIds)
    {
        if (caller.HasAccessToAllStores) return requestedStoreIds.Distinct().ToList();

        var keptOutsideCallerStores = currentStoreIds.Where(storeId => !caller.AssignedStoreIds.Contains(storeId));
        var requestedInsideCallerStores = requestedStoreIds.Where(caller.AssignedStoreIds.Contains);
        return keptOutsideCallerStores.Concat(requestedInsideCallerStores).Distinct().ToList();
    }
}
