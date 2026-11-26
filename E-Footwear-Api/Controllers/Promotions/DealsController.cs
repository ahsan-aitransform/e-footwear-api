using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.BLL.Promotions.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Promotions;

/// <summary>Deals (docx 2.12): one barcode that sells several products at their net prices. Writes also need the restricted code PROMO_MANAGE.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Promotions.Root)]
public sealed class DealsController(PromotionDealService promotionDealService) : ControllerBase
{
    [HttpGet(ApiRoutes.Promotions.Deals)]
    [RequirePermission(PermissionCodes.Settings.Deals.View)]
    public async Task<ActionResult<IReadOnlyList<DealResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await promotionDealService.ListAsync(cancellationToken));

    [HttpGet(ApiRoutes.Promotions.DealVariantByBarcode)]
    [RequirePermission(PermissionCodes.Settings.Deals.View)]
    public async Task<ActionResult<DealVariantLookupResponse>> LookupVariant([FromQuery] string? barcode, CancellationToken cancellationToken) =>
        Ok(await promotionDealService.LookupVariantAsync(barcode, cancellationToken));

    [HttpGet(ApiRoutes.Promotions.DealById)]
    [RequirePermission(PermissionCodes.Settings.Deals.View)]
    public async Task<ActionResult<DealResponse>> Get(Guid dealId, CancellationToken cancellationToken) =>
        Ok(await promotionDealService.GetAsync(dealId, cancellationToken));

    [HttpPost(ApiRoutes.Promotions.Deals)]
    [RequirePermission(PermissionCodes.Settings.Deals.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<ActionResult<DealResponse>> Create(SaveDealRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await promotionDealService.CreateAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.Promotions.DealById)]
    [RequirePermission(PermissionCodes.Settings.Deals.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<ActionResult<DealResponse>> Update(Guid dealId, SaveDealRequest request, CancellationToken cancellationToken) =>
        Ok(await promotionDealService.UpdateAsync(dealId, request, cancellationToken));

    [HttpDelete(ApiRoutes.Promotions.DealById)]
    [RequirePermission(PermissionCodes.Settings.Deals.Delete)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<IActionResult> Delete(Guid dealId, CancellationToken cancellationToken)
    {
        await promotionDealService.DeleteAsync(dealId, cancellationToken);
        return NoContent();
    }
}
