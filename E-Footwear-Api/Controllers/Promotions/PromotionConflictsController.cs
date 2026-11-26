using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.BLL.Promotions.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Promotions;

/// <summary>
/// Promotion Conflicts (Part 18): sizes that two or more promotions cover are sold at the normal price until somebody chooses one. Choosing needs the Approve action of the
/// Promotion Conflict Resolution screen and the restricted code PROMO_CONFLICT_RESOLVE. "Apply schedule now" runs the pricing engine so promotions that started or ended since the
/// last change take effect; it needs PROMO_MANAGE.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Promotions.Root)]
public sealed class PromotionConflictsController(PromotionConflictService promotionConflictService, PromotionRuleService promotionRuleService) : ControllerBase
{
    [HttpGet(ApiRoutes.Promotions.Conflicts)]
    [RequirePermission(PermissionCodes.Promotions.PromotionConflictResolution.View)]
    public async Task<ActionResult<PagedResult<PromotionConflictResponse>>> ListOpen(
        [FromQuery] Guid? storeId, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await promotionConflictService.ListOpenAsync(storeId, pageRequest, cancellationToken));

    [HttpPost(ApiRoutes.Promotions.ResolveConflict)]
    [RequirePermission(PermissionCodes.Promotions.PromotionConflictResolution.Approve)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoConflictResolve)]
    public async Task<IActionResult> Resolve(Guid groupId, ResolvePromotionConflictRequest request, CancellationToken cancellationToken)
    {
        await promotionConflictService.ResolveAsync(groupId, request, cancellationToken);
        return NoContent();
    }

    [HttpPost(ApiRoutes.Promotions.RefreshPrices)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<IActionResult> RefreshPrices(CancellationToken cancellationToken)
    {
        await promotionRuleService.RefreshPricesAsync(cancellationToken);
        return NoContent();
    }
}
