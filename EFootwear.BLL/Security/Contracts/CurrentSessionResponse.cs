namespace EFootwear.BLL.Security.Contracts;

/// <summary>
/// The answer of GET /api/v1/auth/me: who is signed in and what they may do.
/// <paramref name="Audience"/> is a <see cref="SessionAudienceCodes"/> value. For a platform administrator the company,
/// user and permission fields are empty. For the company owner <paramref name="GrantedPermissionCodes"/> lists every
/// permission of the catalog. <paramref name="AccessibleStoreIds"/> are the stores the person may work in (all stores
/// of the company, head office included, when <paramref name="HasAccessToAllStores"/> is true).
/// </summary>
public sealed record CurrentSessionResponse(
    string Audience,
    string DisplayName,
    string EmailOrUsername,
    Guid? PlatformAdministratorId,
    Guid? CompanyId,
    string? CompanyName,
    Guid? UserId,
    string RoleCode,
    bool HasAccessToAllStores,
    IReadOnlyList<Guid> AccessibleStoreIds,
    bool MustChangePassword,
    IReadOnlyList<string> GrantedPermissionCodes);
