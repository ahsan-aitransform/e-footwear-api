using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// The permission set of one user (Part 6: screen- and action-level permissions, per-user overrides). The user's own rows in
/// sec.user_permissions are the truth; role templates only prefill them. Rules: <see cref="PermissionGrantRules"/> (only existing codes,
/// only permissions you hold yourself) and <see cref="UserManagementRules"/> (the owner cannot be reduced; not your own account).
/// </summary>
public sealed class UserPermissionService(
    UserManagementService userManagementService,
    IUserPermissionRepository userPermissionRepository,
    IActivityLogWriter activityLogWriter,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<UserPermissionService> logger)
{
    public async Task<UserPermissionsResponse> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManagementService.FindManageableUserAsync(userId, UserManagementAction.View, cancellationToken);
        if (user.RoleCode == RoleCodes.CompanyOwner)
            return new UserPermissionsResponse(userId, user.RoleCode, HoldsEveryPermissionByRole: true,
                PermissionCatalog.AllPermissionCodes.Order(StringComparer.Ordinal).ToList());

        var granted = await userPermissionRepository.GetGrantedCodesAsync(requestContext.CompanyId, userId, cancellationToken);
        return new UserPermissionsResponse(userId, user.RoleCode, HoldsEveryPermissionByRole: false,
            granted.Order(StringComparer.Ordinal).ToList());
    }

    public async Task ReplaceAsync(Guid userId, ReplaceUserPermissionsRequest request, CancellationToken cancellationToken)
    {
        var user = await userManagementService.FindManageableUserAsync(userId, UserManagementAction.ChangePermissions, cancellationToken);
        var requestedCodes = (request.PermissionCodes ?? []).Distinct(StringComparer.Ordinal).ToList();
        PermissionGrantRules.EnsureCodesAreKnown(requestedCodes);

        var currentCodes = await userPermissionRepository.GetGrantedCodesAsync(requestContext.CompanyId, userId, cancellationToken);
        var change = PermissionGrantRules.Compare(currentCodes, requestedCodes);
        if (change.IsEmpty) return;
        PermissionGrantRules.EnsureNotOwnerOnly(change.AddedCodes);
        PermissionGrantRules.EnsureCanGrant(requestContext.GrantedPermissionCodes, change.AddedCodes);

        await userPermissionRepository.ApplyChangeAsync(requestContext.CompanyId, userId, change, requestContext.UserId, cancellationToken);
        await activityLogWriter.WriteAsync(new ActivityLogEntryRecord(requestContext.CompanyId, SecurityActivityActions.PermissionsChanged,
            SecurityActivityActions.UserPermissionsEntityType, userId.ToString(),
            new { removedCodes = change.RemovedCodes }, new { addedCodes = change.AddedCodes },
            $"{change.AddedCodes.Count} added, {change.RemovedCodes.Count} removed"), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Permissions of user {UserId} ({RoleCode}) changed by {ActorUserId}: +{Added} -{Removed}",
            userId, user.RoleCode, requestContext.UserId, change.AddedCodes.Count, change.RemovedCodes.Count);
    }
}
