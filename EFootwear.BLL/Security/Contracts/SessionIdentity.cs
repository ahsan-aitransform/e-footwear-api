namespace EFootwear.BLL.Security.Contracts;

/// <summary>
/// Who the validated company-user session belongs to. Filled from the database, never from the request.
/// <paramref name="GrantedPermissionCodes"/> are the rows of sec.user_permissions (the company owner additionally holds
/// the whole catalog; see RequestContext).
/// </summary>
public sealed record SessionIdentity(Guid UserId, string RoleCode, bool HasAccessToAllStores,
                                     IReadOnlyList<Guid> AssignedStoreIds, bool MustChangePassword,
                                     IReadOnlySet<string> GrantedPermissionCodes);
