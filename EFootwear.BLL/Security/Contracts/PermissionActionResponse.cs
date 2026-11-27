namespace EFootwear.BLL.Security.Contracts;

/// <summary>IsOwnerOnly: the company owner holds it by role and it cannot be given to anyone else.</summary>
public sealed record PermissionActionResponse(string PermissionCode, string ActionCode, string DisplayName, bool IsOwnerOnly = false);
