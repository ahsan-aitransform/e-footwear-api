namespace EFootwear.BLL.Security.Contracts;

/// <summary>
/// One entry of the template picker. A system suggestion has no Id and its Key is the role code; a company template has an Id and
/// its Key is that Id as text. Suggestions are not business policy: they are starting points only.
/// </summary>
public sealed record RoleTemplateSummaryResponse(string Key, Guid? Id, string Name, string? Description,
                                                 bool IsSystemSuggestion, int PermissionCount);
