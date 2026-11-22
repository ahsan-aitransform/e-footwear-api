using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;

namespace EFootwear.BLL.Common;

/// <summary>
/// Per-request identity. Scoped. It is set once, by the authentication handler, after the session
/// has been validated against the database. Business code reads it through <see cref="ICurrentUser"/>.
/// Either a company user (CompanyId, UserId, role, stores, permissions) or a platform administrator (no company).
/// </summary>
public sealed class RequestContext : ICurrentUser
{
    private static readonly IReadOnlySet<string> NoPermissionCodes = new HashSet<string>();

    private SessionIdentity? _companyUserIdentity;
    private Guid _companyId;
    private PlatformAdministratorIdentity? _platformAdministratorIdentity;

    public bool IsAuthenticated => _companyUserIdentity is not null || _platformAdministratorIdentity is not null;
    public bool IsPlatformAdministrator => _platformAdministratorIdentity is not null;
    public Guid? PlatformAdministratorId => _platformAdministratorIdentity?.AdministratorId;
    public string? PlatformAdministratorUsername => _platformAdministratorIdentity?.Username;
    public string? PlatformAdministratorFullName => _platformAdministratorIdentity?.FullName;

    public Guid CompanyId => _companyUserIdentity is null ? throw NotCompanyUser() : _companyId;
    public Guid UserId => _companyUserIdentity?.UserId ?? throw NotCompanyUser();
    public string RoleCode => _companyUserIdentity?.RoleCode
        ?? (IsPlatformAdministrator ? RoleCodes.PlatformAdmin : throw NotCompanyUser());
    public bool HasAccessToAllStores => IsPlatformAdministrator
        || (_companyUserIdentity?.HasAccessToAllStores ?? throw NotCompanyUser());
    public bool MustChangePassword => _companyUserIdentity?.MustChangePassword ?? false;
    public IReadOnlyList<Guid> AssignedStoreIds => _companyUserIdentity?.AssignedStoreIds ?? [];

    /// <summary>
    /// The permission codes of a company user. The company owner holds the whole catalog (so new screens reach the owner
    /// without a data change); everyone else holds exactly the rows of sec.user_permissions. Platform administrators hold none:
    /// they are controlled by the PlatformAdministratorOnly policy instead.
    /// </summary>
    public IReadOnlySet<string> GrantedPermissionCodes
    {
        get
        {
            if (_companyUserIdentity is null) return NoPermissionCodes;
            return _companyUserIdentity.RoleCode == RoleCodes.CompanyOwner
                ? PermissionCatalog.AllPermissionCodes
                : _companyUserIdentity.GrantedPermissionCodes;
        }
    }

    public bool HasPermission(string permissionCode) => GrantedPermissionCodes.Contains(permissionCode);

    // Nullable views used by the database session (it must work before sign-in, e.g. during login).
    public Guid? CompanyIdOrNull => _companyUserIdentity is null ? null : _companyId;
    public Guid? UserIdOrNull => _companyUserIdentity?.UserId;

    public bool CanAccessStore(Guid storeId) => HasAccessToAllStores || AssignedStoreIds.Contains(storeId);

    /// <summary>The caller as the user-creation rules see it.</summary>
    public Caller ToCaller() => new(IsPlatformAdministrator, _companyUserIdentity?.RoleCode, HasAccessToAllStores,
                                    AssignedStoreIds.ToHashSet());

    public void SetCompanyUser(Guid companyId, SessionIdentity identity)
    {
        if (IsAuthenticated) throw new InvalidOperationException("Request identity is already set.");
        _companyId = companyId;
        _companyUserIdentity = identity;
    }

    public void SetPlatformAdministrator(PlatformAdministratorIdentity identity)
    {
        if (IsAuthenticated) throw new InvalidOperationException("Request identity is already set.");
        _platformAdministratorIdentity = identity;
    }

    private static InvalidOperationException NotCompanyUser() =>
        new("This operation needs a company user; the caller is not one.");
}
