using EFootwear.BLL.Abstractions;
using EFootwear.Domain.Security;

namespace EFootwear.BLL.Common;

/// <summary>
/// Per-request identity. Scoped. It is set once, by the authentication handler, after the session
/// has been validated against the database. Business code reads it through <see cref="ICurrentUser"/>.
/// Either a company user (CompanyId, UserId, role, stores) or a platform administrator (no company).
/// </summary>
public sealed class RequestContext : ICurrentUser
{
    private SessionIdentity? _identity;
    private Guid _companyId;
    private PlatformAdminIdentity? _admin;

    public bool IsAuthenticated => _identity is not null || _admin is not null;
    public bool IsPlatformAdmin => _admin is not null;
    public Guid? PlatformAdminId => _admin?.AdminId;
    public string? PlatformAdminName => _admin?.Username;

    public Guid CompanyId => _identity is null ? throw NotCompanyUser() : _companyId;
    public Guid UserId => _identity?.UserId ?? throw NotCompanyUser();
    public string RoleCode => _identity?.RoleCode ?? (IsPlatformAdmin ? RoleCodes.PlatformAdmin : throw NotCompanyUser());
    public bool AllStores => IsPlatformAdmin || (_identity?.AllStores ?? throw NotCompanyUser());
    public bool MustChangePassword => _identity?.MustChangePassword ?? false;
    public IReadOnlyList<Guid> StoreIds => _identity?.StoreIds ?? [];

    // Nullable views used by the database session (it must work before sign-in, e.g. during login).
    public Guid? CompanyIdOrNull => _identity is null ? null : _companyId;
    public Guid? UserIdOrNull => _identity?.UserId;

    public bool CanAccessStore(Guid storeId) => AllStores || StoreIds.Contains(storeId);

    /// <summary>The caller as the user-creation rules see it.</summary>
    public Caller ToCaller() => new(IsPlatformAdmin, _identity?.RoleCode, AllStores, StoreIds.ToHashSet());

    public void Set(Guid companyId, SessionIdentity identity)
    {
        if (IsAuthenticated) throw new InvalidOperationException("Request identity is already set.");
        _companyId = companyId;
        _identity = identity;
    }

    public void SetPlatformAdmin(PlatformAdminIdentity admin)
    {
        if (IsAuthenticated) throw new InvalidOperationException("Request identity is already set.");
        _admin = admin;
    }

    private static InvalidOperationException NotCompanyUser() =>
        new("This operation needs a company user; the caller is not one.");
}

/// <summary>A platform administrator. Has no company; works across companies on purpose.</summary>
public sealed record PlatformAdminIdentity(Guid AdminId, string Username, string FullName);
