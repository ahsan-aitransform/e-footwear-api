namespace EFootwear.BLL.Security.Contracts;

/// <summary>Create (RowVersion empty) or update (RowVersion from the details) a company role template.</summary>
public sealed record SaveRoleTemplateRequest(string Name, string? Description, IReadOnlyList<string> PermissionCodes, long? RowVersion);
