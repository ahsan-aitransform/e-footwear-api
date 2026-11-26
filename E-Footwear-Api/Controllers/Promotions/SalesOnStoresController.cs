using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.BLL.Promotions.Services;
using EFootwear.Domain.Promotions;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Promotions;

/// <summary>Sale on Stores (docx 2.15): a store-wide discount for the chosen stores between two moments. Writes also need the restricted code PROMO_MANAGE.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Promotions.Root)]
public sealed class SalesOnStoresController(PromotionRuleService promotionRuleService) : ControllerBase
{
    [HttpGet(ApiRoutes.Promotions.SalesOnStores)]
    [RequirePermission(PermissionCodes.Settings.SaleOnStores.View)]
    public async Task<ActionResult<IReadOnlyList<PromotionRuleResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await promotionRuleService.ListAsync(PromotionKindCodes.SaleOnStores, cancellationToken));

    [HttpGet(ApiRoutes.Promotions.SaleOnStoresById)]
    [RequirePermission(PermissionCodes.Settings.SaleOnStores.View)]
    public async Task<ActionResult<PromotionRuleResponse>> Get(Guid saleId, CancellationToken cancellationToken) =>
        Ok(await promotionRuleService.GetAsync(PromotionKindCodes.SaleOnStores, saleId, cancellationToken));

    [HttpPost(ApiRoutes.Promotions.SalesOnStores)]
    [RequirePermission(PermissionCodes.Settings.SaleOnStores.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<ActionResult<PromotionRuleResponse>> Create(SavePromotionRuleRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await promotionRuleService.CreateAsync(PromotionKindCodes.SaleOnStores, request, cancellationToken));

    [HttpPut(ApiRoutes.Promotions.SaleOnStoresById)]
    [RequirePermission(PermissionCodes.Settings.SaleOnStores.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<ActionResult<PromotionRuleResponse>> Update(Guid saleId, SavePromotionRuleRequest request, CancellationToken cancellationToken) =>
        Ok(await promotionRuleService.UpdateAsync(PromotionKindCodes.SaleOnStores, saleId, request, cancellationToken));

    [HttpDelete(ApiRoutes.Promotions.SaleOnStoresById)]
    [RequirePermission(PermissionCodes.Settings.SaleOnStores.Delete)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<IActionResult> Delete(Guid saleId, CancellationToken cancellationToken)
    {
        await promotionRuleService.DeleteAsync(PromotionKindCodes.SaleOnStores, saleId, cancellationToken);
        return NoContent();
    }
}
