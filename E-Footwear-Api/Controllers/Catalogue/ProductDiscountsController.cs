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
/// Product discounts: Change Discounts (docx 4.9), Product Discounts On Multiple Stores (4.11) and Remove Discounts and Reverse Price (4.12). A discount is a
/// promotion rule for one store; the response says how many sizes now have overlapping discounts that need an administrator's decision.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.ProductDiscounts.Root)]
public sealed class ProductDiscountsController(ProductDiscountService productDiscountService) : ControllerBase
{
    [HttpPost(ApiRoutes.ProductDiscounts.StockRangePreview)]
    [RequirePermission(PermissionCodes.Products.ChangeDiscounts.View)]
    public async Task<ActionResult<PagedResult<StockRangeDiscountRowResponse>>> PreviewStockRange(
        StockRangeDiscountRequest request, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        await productDiscountService.ListStockRangeCandidatesAsync(request, pageQuery.ToPageRequest(), cancellationToken);

    [HttpPost(ApiRoutes.ProductDiscounts.StockRange)]
    [RequirePermission(PermissionCodes.Products.ChangeDiscounts.Edit)]
    public async Task<ActionResult<ProductDiscountAppliedResponse>> ApplyStockRange(StockRangeDiscountRequest request, CancellationToken cancellationToken) =>
        await productDiscountService.ApplyStockRangeDiscountAsync(request, cancellationToken);

    /// <summary>?productId=... : the product in each store the caller may use, with the stock there and the discount it has now.</summary>
    [HttpGet(ApiRoutes.ProductDiscounts.StoreRows)]
    [RequirePermission(PermissionCodes.Products.ProductDiscountsOnMultipleStores.View)]
    public async Task<ActionResult<IReadOnlyList<ProductStoreDiscountRowResponse>>> ListStoreRows([FromQuery] Guid productId, CancellationToken cancellationToken) =>
        Ok(await productDiscountService.ListStoreDiscountRowsAsync(productId, cancellationToken));

    [HttpPost(ApiRoutes.ProductDiscounts.OnStores)]
    [RequirePermission(PermissionCodes.Products.ProductDiscountsOnMultipleStores.Edit)]
    public async Task<ActionResult<ProductDiscountAppliedResponse>> ApplyOnStores(ProductDiscountOnStoresRequest request, CancellationToken cancellationToken) =>
        await productDiscountService.ApplyDiscountOnStoresAsync(request, cancellationToken);

    [HttpPost(ApiRoutes.ProductDiscounts.Remove)]
    [RequirePermission(PermissionCodes.Products.RemoveDiscountsAndReversePrice.Edit)]
    public async Task<ActionResult<ProductDiscountAppliedResponse>> Remove(RemoveProductDiscountsRequest request, CancellationToken cancellationToken) =>
        await productDiscountService.RemoveDiscountsAsync(request, cancellationToken);
}
