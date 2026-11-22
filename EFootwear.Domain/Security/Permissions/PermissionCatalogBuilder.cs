namespace EFootwear.Domain.Security.Permissions;

/// <summary>
/// Fluent helper used only by <see cref="PermissionCatalogDefinitions"/>. It builds the permission code
/// <c>MODULE.SCREEN.ACTION</c> (upper snake case, dots between the three parts) so the format lives in one place.
/// </summary>
internal sealed class PermissionCatalogBuilder
{
    private readonly List<PermissionModuleBuilder> moduleBuilders = [];

    public PermissionModuleBuilder Module(string moduleCode, string displayName)
    {
        var moduleBuilder = new PermissionModuleBuilder(moduleCode, displayName);
        moduleBuilders.Add(moduleBuilder);
        return moduleBuilder;
    }

    public IReadOnlyList<PermissionModuleDefinition> Build() =>
        moduleBuilders.Select(moduleBuilder => moduleBuilder.Build()).ToList();
}

internal sealed class PermissionModuleBuilder(string moduleCode, string displayName)
{
    private readonly List<PermissionScreenDefinition> screens = [];

    /// <summary>A screen whose actions are named by an action set (View, Add, Edit, ...).</summary>
    public PermissionModuleBuilder Screen(string screenCode, string screenDisplayName,
                                          IReadOnlyList<string> actionCodes, string? subGroupCode = null)
    {
        var actions = actionCodes
            .Select(actionCode => new PermissionActionDefinition(
                PermissionCode: $"{moduleCode}.{screenCode}.{actionCode}",
                ActionCode: actionCode,
                DisplayName: PermissionActionCodes.DisplayNameOf(actionCode),
                Description: $"{screenDisplayName}: {PermissionActionCodes.DisplayNameOf(actionCode)}"))
            .ToList();
        screens.Add(new PermissionScreenDefinition(screenCode, screenDisplayName, subGroupCode, actions));
        return this;
    }

    /// <summary>
    /// A group of legacy restricted operations. The permission code IS the legacy code (for example SALE_CREATE),
    /// because the SQL functions check that exact code through sec.require_permission.
    /// </summary>
    public PermissionModuleBuilder RestrictedOperationGroup(string screenCode, string screenDisplayName,
                                                            params (string LegacyCode, string Description)[] operations)
    {
        var actions = operations
            .Select(operation => new PermissionActionDefinition(operation.LegacyCode, operation.LegacyCode,
                                                                operation.Description, operation.Description))
            .ToList();
        screens.Add(new PermissionScreenDefinition(screenCode, screenDisplayName, null, actions));
        return this;
    }

    public PermissionModuleDefinition Build() => new(moduleCode, displayName, screens);
}
