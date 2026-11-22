namespace EFootwear.Domain.Security.Permissions;

/// <summary>One permission together with where it sits in the tree. <paramref name="SortOrder"/> starts at 1.</summary>
public sealed record PermissionCatalogEntry(string ModuleCode, string ScreenCode, PermissionActionDefinition Action,
                                            int SortOrder);
