using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Catalogue.Services;
using EFootwear.BLL.Common;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Catalogue;

/// <summary>Price bands: named ranges of list prices used by the price reports and analytics. Ranges of one company cannot overlap.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.PriceBands.Root)]
public sealed class PriceBandsController(PriceBandService priceBandService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Products.PriceBands.View)]
    public async Task<ActionResult<IReadOnlyList<PriceBandResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await priceBandService.ListAsync(cancellationToken));

    [HttpGet(ApiRoutes.PriceBands.ById)]
    [RequirePermission(PermissionCodes.Products.PriceBands.View)]
    public async Task<ActionResult<PriceBandResponse>> Get(Guid priceBandId, CancellationToken cancellationToken) =>
        await priceBandService.GetAsync(priceBandId, cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCodes.Products.PriceBands.Add)]
    public async Task<ActionResult<CreatedIdResponse>> Create(SavePriceBandRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await priceBandService.CreateAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.PriceBands.ById)]
    [RequirePermission(PermissionCodes.Products.PriceBands.Edit)]
    public async Task<IActionResult> Update(Guid priceBandId, SavePriceBandRequest request, CancellationToken cancellationToken)
    {
        await priceBandService.UpdateAsync(priceBandId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.PriceBands.ById)]
    [RequirePermission(PermissionCodes.Products.PriceBands.Delete)]
    public async Task<IActionResult> Delete(Guid priceBandId, CancellationToken cancellationToken)
    {
        await priceBandService.DeleteAsync(priceBandId, cancellationToken);
        return NoContent();
    }
}
