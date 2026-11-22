namespace EFootwear.BLL.Security.Contracts;

/// <summary>The complete new set of permission codes. Only permissions the caller holds may be added.</summary>
public sealed record ReplaceUserPermissionsRequest(IReadOnlyList<string> PermissionCodes);
