using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Core;

/// <summary>
/// The stores the signed-in company user may work in (row-level security and core.store_allowed do the filtering).
/// Any company user may read their own list; it needs no screen permission because every screen needs it to show the store picker.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Stores.Root)]
public sealed class StoresController(StoreService storeService) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<StoreResponse>> List(CancellationToken cancellationToken) =>
        await storeService.ListAsync(cancellationToken);
}
