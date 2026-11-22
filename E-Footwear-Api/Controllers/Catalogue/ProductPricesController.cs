using E_Footwear_Api.Authorization;
using E_Footwear_Api.Binding;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Catalogue.Services;
using EFootwear.BLL.Common;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Catalogue;

/// <summary>
/// Bulk price changes. Each screen has a preview (the sizes that would change, paged, nothing saved) and an apply. A request with a clientTransactionId
/// is idempotent. The preview needs the screen's View right, the change needs Edit.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.ProductPrices.Root)]
public sealed class ProductPricesController(BulkPriceChangeService bulkPriceChangeService) : ControllerBase
{
    [HttpPost(ApiRoutes.ProductPrices.OldToNewPreview)]
    [RequirePermission(PermissionCodes.Products.ChangePricesOldToNew.View)]
    public async Task<ActionResult<PagedResult<PriceChangeRowResponse>>> PreviewOldToNew(
        OldToNewPriceChangeRequest request, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        await bulkPriceChangeService.PreviewOldToNewAsync(request, pageQuery.ToPageRequest(), cancellationToken);

    [HttpPost(ApiRoutes.ProductPrices.OldToNew)]
    [RequirePermission(PermissionCodes.Products.ChangePricesOldToNew.Edit)]
    public async Task<ActionResult<PriceChangeAppliedResponse>> ApplyOldToNew(OldToNewPriceChangeRequest request, CancellationToken cancellationToken) =>
        await bulkPriceChangeService.ApplyOldToNewAsync(request, cancellationToken);

    [HttpPost(ApiRoutes.ProductPrices.PercentagePreview)]
    [RequirePermission(PermissionCodes.Products.ChangePricesByPercentage.View)]
    public async Task<ActionResult<PagedResult<PriceChangeRowResponse>>> PreviewPercentage(
        PercentagePriceChangeRequest request, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        await bulkPriceChangeService.PreviewPercentageAsync(request, pageQuery.ToPageRequest(), cancellationToken);

    [HttpPost(ApiRoutes.ProductPrices.Percentage)]
    [RequirePermission(PermissionCodes.Products.ChangePricesByPercentage.Edit)]
    public async Task<ActionResult<PriceChangeAppliedResponse>> ApplyPercentage(PercentagePriceChangeRequest request, CancellationToken cancellationToken) =>
        await bulkPriceChangeService.ApplyPercentageAsync(request, cancellationToken);

    /// <summary>Product Discounts (docx 4.10): a new sale price for every size of the selected products.</summary>
    [HttpPost(ApiRoutes.ProductPrices.NewSalePrice)]
    [RequirePermission(PermissionCodes.Products.ProductDiscounts.Edit)]
    public async Task<ActionResult<PriceChangeAppliedResponse>> SetNewSalePrice(SetNewSalePriceRequest request, CancellationToken cancellationToken) =>
        await bulkPriceChangeService.SetNewSalePriceAsync(request, cancellationToken);
}
