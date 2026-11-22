namespace EFootwear.BLL.Security.Contracts;

/// <summary>
/// A platform administrator names <paramref name="CompanyId"/>; a company user leaves it empty (their own company is used).
/// <paramref name="PermissionCodes"/> is the permission set chosen on the screen; empty means the suggested set of the role
/// (DefaultRolePermissionTemplates). <paramref name="QuickActionCodes"/> are the toolbar buttons (docx Standard Menu).
/// </summary>
public sealed record CreateUserRequest(Guid? CompanyId, string Email, string FullName, string RoleCode,
                                       IReadOnlyList<Guid>? StoreIds, string Password,
                                       IReadOnlyList<string>? PermissionCodes = null,
                                       IReadOnlyList<string>? QuickActionCodes = null);
