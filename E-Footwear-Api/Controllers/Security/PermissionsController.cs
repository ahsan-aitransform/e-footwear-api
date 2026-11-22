using E_Footwear_Api.Routing;
using EFootwear.BLL.Security.Contracts;
using EFootwear.BLL.Security.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Security;

/// <summary>The permission tree (modules, screens, actions). Any signed-in person may read it; it holds no company data.</summary>
[ApiController]
[Authorize]
[Route(ApiRoutes.Permissions.Root)]
public sealed class PermissionsController(PermissionCatalogService permissionCatalogService) : ControllerBase
{
    [HttpGet(ApiRoutes.Permissions.Catalog)]
    public ActionResult<PermissionCatalogResponse> GetCatalog() => permissionCatalogService.GetCatalog();
}
