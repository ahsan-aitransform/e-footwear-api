namespace EFootwear.Domain.Security.Permissions;

/// <summary>A top-level module of the application menu, for example Products or Invoice, with its screens.</summary>
public sealed record PermissionModuleDefinition(string ModuleCode, string DisplayName,
                                                IReadOnlyList<PermissionScreenDefinition> Screens);
