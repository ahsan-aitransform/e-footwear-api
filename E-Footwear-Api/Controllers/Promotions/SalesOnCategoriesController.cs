using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.BLL.Promotions.Services;
using EFootwear.Domain.Promotions;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Promotions;

/// <summary>Sale on Categories (docx 2.16): a discount on whole categories in the chosen stores between two moments. Writes also need the restricted code PROMO_MANAGE.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Promotions.Root)]
public sealed class SalesOnCategoriesController(PromotionRuleService promotionRuleService) : ControllerBase
{
    [HttpGet(ApiRoutes.Promotions.SalesOnCategories)]
    [RequirePermission(PermissionCodes.Settings.SaleOnCategories.View)]
    public async Task<ActionResult<IReadOnlyList<PromotionRuleResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await promotionRuleService.ListAsync(PromotionKindCodes.SaleOnCategories, cancellationToken));

    [HttpGet(ApiRoutes.Promotions.SaleOnCategoriesCategoryOptions)]
    [RequirePermission(PermissionCodes.Settings.SaleOnCategories.View)]
    public async Task<ActionResult<IReadOnlyList<PromotionCategoryResponse>>> ListCategoryOptions(CancellationToken cancellationToken) =>
        Ok(await promotionRuleService.ListCategoryOptionsAsync(cancellationToken));

    [HttpGet(ApiRoutes.Promotions.SaleOnCategoriesById)]
    [RequirePermission(PermissionCodes.Settings.SaleOnCategories.View)]
    public async Task<ActionResult<PromotionRuleResponse>> Get(Guid saleId, CancellationToken cancellationToken) =>
        Ok(await promotionRuleService.GetAsync(PromotionKindCodes.SaleOnCategories, saleId, cancellationToken));

    [HttpPost(ApiRoutes.Promotions.SalesOnCategories)]
    [RequirePermission(PermissionCodes.Settings.SaleOnCategories.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<ActionResult<PromotionRuleResponse>> Create(SavePromotionRuleRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await promotionRuleService.CreateAsync(PromotionKindCodes.SaleOnCategories, request, cancellationToken));

    [HttpPut(ApiRoutes.Promotions.SaleOnCategoriesById)]
    [RequirePermission(PermissionCodes.Settings.SaleOnCategories.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<ActionResult<PromotionRuleResponse>> Update(Guid saleId, SavePromotionRuleRequest request, CancellationToken cancellationToken) =>
        Ok(await promotionRuleService.UpdateAsync(PromotionKindCodes.SaleOnCategories, saleId, request, cancellationToken));

    [HttpDelete(ApiRoutes.Promotions.SaleOnCategoriesById)]
    [RequirePermission(PermissionCodes.Settings.SaleOnCategories.Delete)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<IActionResult> Delete(Guid saleId, CancellationToken cancellationToken)
    {
        await promotionRuleService.DeleteAsync(PromotionKindCodes.SaleOnCategories, saleId, cancellationToken);
        return NoContent();
    }
}
