namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// Read access to the permission catalog (see <see cref="PermissionCatalogDefinitions"/> for how it is defined).
/// Built once; safe to read from any thread.
/// </summary>
public static class PermissionCatalog
{
    public const string RestrictedOperationsModuleCode = "RESTRICTED_OPERATIONS";

    /// <summary>Modules in menu order, each with its screens and their actions.</summary>
    public static IReadOnlyList<PermissionModuleDefinition> Modules { get; } = PermissionCatalogDefinitions.BuildModules();

    /// <summary>Every permission, in menu order (the position is stored as sec.permissions.sort_order).</summary>
    public static IReadOnlyList<PermissionCatalogEntry> AllEntries { get; } = FlattenEntries();

    /// <summary>Every permission code of the catalog. The company owner holds all of them.</summary>
    public static IReadOnlySet<string> AllPermissionCodes { get; } =
        FlattenEntries().Select(entry => entry.Action.PermissionCode).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnownPermissionCode(string permissionCode) => AllPermissionCodes.Contains(permissionCode);

    private static IReadOnlyList<PermissionCatalogEntry> FlattenEntries()
    {
        var entries = new List<PermissionCatalogEntry>();
        var sortOrder = 0;
        foreach (var module in Modules)
            foreach (var screen in module.Screens)
                foreach (var action in screen.Actions)
                    entries.Add(new PermissionCatalogEntry(module.ModuleCode, screen.ScreenCode, action, ++sortOrder));

        var duplicate = entries.GroupBy(entry => entry.Action.PermissionCode).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"The permission catalog defines {duplicate.Key} more than once.");
        return entries;
    }
}
