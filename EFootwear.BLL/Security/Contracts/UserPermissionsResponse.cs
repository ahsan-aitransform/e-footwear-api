namespace EFootwear.BLL.Security.Contracts;

/// <summary>
/// The permission codes of one user. For the company owner <paramref name="HoldsEveryPermissionByRole"/> is true and the list holds
/// the whole catalog (the owner needs no rows and cannot be reduced).
/// </summary>
public sealed record UserPermissionsResponse(Guid UserId, string RoleCode, bool HoldsEveryPermissionByRole,
                                             IReadOnlyList<string> GrantedPermissionCodes);
