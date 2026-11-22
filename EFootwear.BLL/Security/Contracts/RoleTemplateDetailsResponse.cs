namespace EFootwear.BLL.Security.Contracts;

public sealed record RoleTemplateDetailsResponse(string Key, Guid? Id, string Name, string? Description, bool IsSystemSuggestion,
                                                 IReadOnlyList<string> PermissionCodes, long RowVersion);
