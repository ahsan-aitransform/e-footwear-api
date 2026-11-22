using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// User management (docx 1.1 Create New User, 1.3 Users Account). Who may create which role, in which stores, is decided by
/// <see cref="UserCreationRules"/>; who may change or remove another user by <see cref="UserManagementRules"/>. This service decides the
/// company, applies the rules and saves.
///
/// Company scope: a platform administrator names the company when listing and creating. A company user is always bound to their own
/// company; naming another company is refused. Editing, deleting and the other actions are for company users only.
/// </summary>
public sealed class UserManagementService(
    IUserRepository userRepository,
    IUserQuickActionRepository userQuickActionRepository,
    IStoreRepository storeRepository,
    IPlatformRepository platformRepository,
    IActivityLogWriter activityLogWriter,
    IPasswordHasher passwordHasher,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<UserManagementService> logger)
{
    public const string UserCreatedActionCode = "USER_CREATED";

    public async Task<UserCreationOptionsResponse> GetCreationOptionsAsync(Guid? requestedCompanyId,
                                                                           CancellationToken cancellationToken)
    {
        var companyId = await ResolveCompanyAsync(requestedCompanyId, cancellationToken);
        var creatableRoles = UserCreationRules.CreatableRoles(requestContext.ToCaller());

        // Platform administrators see every store of the company. Company users see their accessible stores.
        var allStores = requestContext.IsPlatformAdministrator
            ? await storeRepository.ListForCompanyAsync(companyId, cancellationToken)
            : await storeRepository.ListAccessibleAsync(cancellationToken);
        var sellingStores = allStores.Where(store => !store.IsHeadOffice && store.IsActive).ToList();

        return new UserCreationOptionsResponse(creatableRoles, sellingStores,
            NeedsStore: creatableRoles.Any(roleCode => roleCode != RoleCodes.CompanyOwner));
    }

    public async Task<IReadOnlyList<UserListItemResponse>> ListAsync(Guid? requestedCompanyId,
                                                                     CancellationToken cancellationToken)
    {
        var companyId = await ResolveCompanyAsync(requestedCompanyId, cancellationToken);

        // A store manager sees only the people who work in its stores. Owners and platform admins see all.
        IReadOnlyList<Guid>? storeScope = requestContext.IsPlatformAdministrator || requestContext.HasAccessToAllStores
            ? null
            : requestContext.AssignedStoreIds.ToList();
        return await userRepository.ListAsync(companyId, storeScope, cancellationToken);
    }

    public async Task<UserDetailsResponse> GetDetailsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var details = await FindManageableUserAsync(userId, UserManagementAction.View, cancellationToken);
        return details;
    }

    public async Task<Guid> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var companyId = await ResolveCompanyAsync(request.CompanyId, cancellationToken);

        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var fullName = (request.FullName ?? "").Trim();
        UserCreationRules.EnsureValidAccount(email, fullName, request.Password ?? "");

        var storeIds = (request.StoreIds ?? []).Distinct().ToList();
        UserCreationRules.EnsureCanCreate(requestContext.ToCaller(), request.RoleCode, storeIds);

        if (storeIds.Count > 0)
        {
            var validStoreIds = await storeRepository.ActiveSellingStoreIdsAsync(companyId, storeIds, cancellationToken);
            if (validStoreIds.Count != storeIds.Count)
                throw new DomainException(ErrorKind.Validation, "One or more stores are not valid for this company.");
        }

        var chosenPermissionCodes = PrepareChosenPermissionCodes(request);
        var quickActionCodes = request.QuickActionCodes ?? [];
        QuickActionCodes.EnsureAllKnown(quickActionCodes);

        if (await userRepository.EmailExistsAsync(email, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"The email {email} is already in use.");

        var newUser = new NewUserRecord(email, passwordHasher.Hash(request.Password!), fullName, request.RoleCode,
                                        AllStores: request.RoleCode == RoleCodes.CompanyOwner);
        var createdByUserId = requestContext.IsPlatformAdministrator ? (Guid?)null : requestContext.UserId;
        var userId = await userRepository.InsertAsync(companyId, newUser, createdByUserId, cancellationToken);
        if (storeIds.Count > 0)
            await userRepository.AddStoresAsync(companyId, userId, storeIds, cancellationToken);

        // The permissions chosen on the screen win. Without a choice, the role template (a SUGGESTION, see
        // DefaultRolePermissionTemplates) becomes the user's own permissions, which can then be changed one by one. The company owner
        // holds everything by role and gets no rows.
        if (DefaultRolePermissionTemplates.IsCopiedToUserPermissions(request.RoleCode))
        {
            var codesToCopy = chosenPermissionCodes ?? DefaultRolePermissionTemplates.PermissionCodesForRole(request.RoleCode);
            await userRepository.GrantPermissionsAsync(companyId, userId, codesToCopy, createdByUserId, cancellationToken);
            await activityLogWriter.WriteAsync(new ActivityLogEntryRecord(companyId, SecurityActivityActions.PermissionsChanged,
                SecurityActivityActions.UserPermissionsEntityType, userId.ToString(), null,
                new { source = chosenPermissionCodes is null ? "ROLE_TEMPLATE" : "CHOSEN_ON_CREATE", request.RoleCode, count = codesToCopy.Count },
                "Permissions set when the user was created"), cancellationToken);
        }

        if (quickActionCodes.Count > 0)
            await userQuickActionRepository.ReplaceAsync(companyId, userId, quickActionCodes, cancellationToken);

        if (requestContext.IsPlatformAdministrator)
            await platformRepository.LogAdministratorActionAsync(requestContext.PlatformAdministratorId!.Value,
                UserCreatedActionCode, companyId,
                new { userId, email, role = request.RoleCode, storeIds }, cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("User {NewUserId} created in company {CompanyId} with role {RoleCode} by {Actor}",
            userId, companyId, request.RoleCode,
            requestContext.IsPlatformAdministrator
                ? $"platform admin {requestContext.PlatformAdministratorId}"
                : $"user {requestContext.UserId}");
        return userId;
    }

    /// <summary>Edit User (docx 1.3 Edit): name, email, role and stores. A stale <c>RowVersion</c> is refused with a conflict.</summary>
    public async Task UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var current = await FindManageableUserAsync(userId, UserManagementAction.EditProfile, cancellationToken);
        var caller = requestContext.ToCaller();
        var isOwnAccount = current.Id == requestContext.UserId;

        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var fullName = (request.FullName ?? "").Trim();
        UserCreationRules.EnsureValidEmailAndFullName(email, fullName);

        var requestedStoreIds = (request.StoreIds ?? current.StoreIds).Distinct().ToList();
        IReadOnlyList<Guid> finalStoreIds = current.StoreIds;
        if (isOwnAccount)
        {
            if (request.RoleCode != current.RoleCode)
                throw new DomainException(ErrorKind.Forbidden, "You cannot change your own role.");
        }
        else
        {
            var roleOrStoresChanged = request.RoleCode != current.RoleCode
                || !requestedStoreIds.ToHashSet().SetEquals(current.StoreIds);
            if (roleOrStoresChanged)
            {
                UserManagementRules.EnsureCanManage(caller, requestContext.UserId, ToSummary(current), UserManagementAction.ChangeRoleOrStores);
                UserCreationRules.EnsureCanCreate(caller, request.RoleCode, requestedStoreIds);
                var validStoreIds = await storeRepository.ActiveSellingStoreIdsAsync(requestContext.CompanyId, requestedStoreIds, cancellationToken);
                if (validStoreIds.Count != requestedStoreIds.Count)
                    throw new DomainException(ErrorKind.Validation, "One or more stores are not valid for this company.");
                finalStoreIds = UserManagementRules.MergeStoreAssignments(caller, current.StoreIds, requestedStoreIds);
                if (request.RoleCode == RoleCodes.StoreManager && finalStoreIds.Count != 1)
                    throw new DomainException(ErrorKind.Validation, "A store manager heads exactly one store.");
            }
        }

        if (email != current.Email && await userRepository.EmailExistsAsync(email, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"The email {email} is already in use.");

        await userRepository.UpdateProfileAsync(requestContext.CompanyId, userId,
            new UserProfileUpdateRecord(email, fullName, request.RoleCode, HasAccessToAllStores: request.RoleCode == RoleCodes.CompanyOwner),
            request.RowVersion, cancellationToken);

        if (!finalStoreIds.ToHashSet().SetEquals(current.StoreIds))
        {
            await userRepository.ReplaceStoresAsync(requestContext.CompanyId, userId, finalStoreIds, cancellationToken);
            await activityLogWriter.WriteAsync(new ActivityLogEntryRecord(requestContext.CompanyId,
                SecurityActivityActions.StoreAssignmentsChanged, SecurityActivityActions.UserStoresEntityType, userId.ToString(),
                new { storeIds = current.StoreIds }, new { storeIds = finalStoreIds }, null), cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("User {UserId} edited by {ActorUserId}", userId, requestContext.UserId);
    }

    /// <summary>Delete (docx 1.3): a soft delete. The company owner and your own account cannot be deleted.</summary>
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var current = await FindManageableUserAsync(userId, UserManagementAction.Delete, cancellationToken);
        await userRepository.SoftDeleteAsync(requestContext.CompanyId, userId, requestContext.UserId, cancellationToken);
        await activityLogWriter.WriteAsync(new ActivityLogEntryRecord(requestContext.CompanyId, SecurityActivityActions.UserDeleted,
            SecurityActivityActions.UserEntityType, userId.ToString(), new { current.Email, current.RoleCode }, null,
            "User deleted; sessions ended"), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("User {UserId} deleted by {ActorUserId}", userId, requestContext.UserId);
    }

    /// <summary>Loads a user of the caller's company and checks that the caller may do <paramref name="action"/> to it.</summary>
    internal async Task<UserDetailsResponse> FindManageableUserAsync(Guid userId, UserManagementAction action,
                                                                    CancellationToken cancellationToken)
    {
        var details = await userRepository.FindDetailsAsync(requestContext.CompanyId, userId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "User not found.");
        UserManagementRules.EnsureCanManage(requestContext.ToCaller(), requestContext.UserId, ToSummary(details), action);
        return details;
    }

    private static ManagedUserSummary ToSummary(UserDetailsResponse details) =>
        new(details.Id, details.RoleCode, details.StoreIds.ToHashSet());

    /// <summary>The permission codes chosen on the Create screen, validated; null when the role template should be used.</summary>
    private IReadOnlyList<string>? PrepareChosenPermissionCodes(CreateUserRequest request)
    {
        if (request.PermissionCodes is null || request.PermissionCodes.Count == 0) return null;
        if (requestContext.IsPlatformAdministrator)
            throw new DomainException(ErrorKind.Validation, "Platform administrators create users with the suggested permissions of the role.");
        var chosen = request.PermissionCodes.Distinct(StringComparer.Ordinal).ToList();
        PermissionGrantRules.EnsureCodesAreKnown(chosen);
        PermissionGrantRules.EnsureCanGrant(requestContext.GrantedPermissionCodes, chosen);
        return chosen;
    }

    /// <summary>Platform administrators name the company. Company users are always their own company.</summary>
    private async Task<Guid> ResolveCompanyAsync(Guid? requestedCompanyId, CancellationToken cancellationToken)
    {
        if (requestContext.IsPlatformAdministrator)
        {
            if (requestedCompanyId is null || requestedCompanyId == Guid.Empty)
                throw new DomainException(ErrorKind.Validation, "Choose the company.");
            if (!await platformRepository.CompanyExistsAsync(requestedCompanyId.Value, cancellationToken))
                throw new DomainException(ErrorKind.NotFound, "Company not found.");
            return requestedCompanyId.Value;
        }

        if (!requestContext.IsAuthenticated)
            throw new DomainException(ErrorKind.Unauthorized, "Sign in first.");
        if (requestedCompanyId is { } requested && requested != requestContext.CompanyId)
            throw new DomainException(ErrorKind.Forbidden, "You can only manage users of your own company.");
        return requestContext.CompanyId;
    }
}
