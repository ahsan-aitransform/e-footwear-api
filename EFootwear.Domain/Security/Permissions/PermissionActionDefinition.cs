namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// One permission: one action on one screen. <paramref name="PermissionCode"/> is what is stored in
/// sec.user_permissions and checked by <c>[RequirePermission]</c>. <paramref name="Description"/> is stored in
/// sec.permissions.description; it is not sent to the client.
/// </summary>
public sealed record PermissionActionDefinition(string PermissionCode, string ActionCode, string DisplayName,
                                                string Description);
