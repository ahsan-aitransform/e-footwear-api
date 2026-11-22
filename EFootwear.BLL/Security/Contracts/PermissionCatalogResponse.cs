namespace EFootwear.BLL.Security.Contracts;

/// <summary>The permission tree: modules, their screens, and the actions of each screen.</summary>
public sealed record PermissionCatalogResponse(IReadOnlyList<PermissionModuleResponse> Modules);
