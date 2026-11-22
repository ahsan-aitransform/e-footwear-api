using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Security.Permissions;

namespace EFootwear.BLL.Security.Services;

/// <summary>Turns the in-code permission catalog into the tree the user interface draws (GET /permissions/catalog).</summary>
public sealed class PermissionCatalogService
{
    public PermissionCatalogResponse GetCatalog() => new(
        PermissionCatalog.Modules
            .Select(module => new PermissionModuleResponse(
                module.ModuleCode,
                module.DisplayName,
                module.Screens
                    .Select(screen => new PermissionScreenResponse(
                        screen.ScreenCode,
                        screen.DisplayName,
                        screen.SubGroupCode,
                        screen.Actions
                            .Select(action => new PermissionActionResponse(action.PermissionCode, action.ActionCode, action.DisplayName))
                            .ToList()))
                    .ToList()))
            .ToList());
}
