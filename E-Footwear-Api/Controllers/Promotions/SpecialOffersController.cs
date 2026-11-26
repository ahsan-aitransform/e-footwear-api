using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.BLL.Promotions.Services;
using EFootwear.Domain.Promotions;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Promotions;

/// <summary>
/// Special Offers (docx 2.13) and Special Offer Details (docx 2.14). Each screen has its own permissions. Creating, changing and ending a promotion also need the restricted
/// code PROMO_MANAGE ("Create and end promotions"). The database engine makes the prices; the services run it after each change.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Promotions.Root)]
public sealed class SpecialOffersController(PromotionRuleService promotionRuleService, SpecialOfferLineService specialOfferLineService) : ControllerBase
{
    [HttpGet(ApiRoutes.Promotions.SpecialOffers)]
    [RequirePermission(PermissionCodes.Settings.SpecialOffers.View)]
    public async Task<ActionResult<IReadOnlyList<PromotionRuleResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await promotionRuleService.ListAsync(PromotionKindCodes.SpecialOffer, cancellationToken));

    [HttpGet(ApiRoutes.Promotions.SpecialOfferById)]
    [RequirePermission(PermissionCodes.Settings.SpecialOffers.View)]
    public async Task<ActionResult<PromotionRuleResponse>> Get(Guid offerId, CancellationToken cancellationToken) =>
        Ok(await promotionRuleService.GetAsync(PromotionKindCodes.SpecialOffer, offerId, cancellationToken));

    [HttpPost(ApiRoutes.Promotions.SpecialOffers)]
    [RequirePermission(PermissionCodes.Settings.SpecialOffers.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<ActionResult<PromotionRuleResponse>> Create(SavePromotionRuleRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await promotionRuleService.CreateAsync(PromotionKindCodes.SpecialOffer, request, cancellationToken));

    [HttpPut(ApiRoutes.Promotions.SpecialOfferById)]
    [RequirePermission(PermissionCodes.Settings.SpecialOffers.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<ActionResult<PromotionRuleResponse>> Update(Guid offerId, SavePromotionRuleRequest request, CancellationToken cancellationToken) =>
        Ok(await promotionRuleService.UpdateAsync(PromotionKindCodes.SpecialOffer, offerId, request, cancellationToken));

    [HttpDelete(ApiRoutes.Promotions.SpecialOfferById)]
    [RequirePermission(PermissionCodes.Settings.SpecialOffers.Delete)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<IActionResult> Delete(Guid offerId, CancellationToken cancellationToken)
    {
        await promotionRuleService.DeleteAsync(PromotionKindCodes.SpecialOffer, offerId, cancellationToken);
        return NoContent();
    }

    [HttpGet(ApiRoutes.Promotions.SpecialOfferLines)]
    [RequirePermission(PermissionCodes.Settings.SpecialOfferDetails.View)]
    public async Task<ActionResult<IReadOnlyList<SpecialOfferLineResponse>>> ListLines(Guid offerId, CancellationToken cancellationToken) =>
        Ok(await specialOfferLineService.ListAsync(offerId, cancellationToken));

    [HttpPost(ApiRoutes.Promotions.SpecialOfferLines)]
    [RequirePermission(PermissionCodes.Settings.SpecialOfferDetails.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<ActionResult<AddSpecialOfferLinesResponse>> AddLines(Guid offerId, AddSpecialOfferLinesRequest request, CancellationToken cancellationToken) =>
        Ok(await specialOfferLineService.AddAsync(offerId, request, cancellationToken));

    [HttpPut(ApiRoutes.Promotions.SpecialOfferLineById)]
    [RequirePermission(PermissionCodes.Settings.SpecialOfferDetails.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<IActionResult> UpdateLine(Guid offerId, Guid lineId, UpdateSpecialOfferLineRequest request, CancellationToken cancellationToken)
    {
        await specialOfferLineService.UpdateAsync(offerId, lineId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.Promotions.SpecialOfferLineById)]
    [RequirePermission(PermissionCodes.Settings.SpecialOfferDetails.Delete)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Promotions.PromoManage)]
    public async Task<IActionResult> DeleteLine(Guid offerId, Guid lineId, CancellationToken cancellationToken)
    {
        await specialOfferLineService.DeleteAsync(offerId, lineId, cancellationToken);
        return NoContent();
    }
}
