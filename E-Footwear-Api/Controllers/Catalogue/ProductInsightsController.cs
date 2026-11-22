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

/// <summary>The read-only product screens: Check Price (docx 4.5), Product Ledger (4.4), Products Stock with Sizes On Multiple Stores (4.14) and the Product Report (4.15).</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Products.Root)]
public sealed class ProductInsightsController(ProductListingService productListingService, ProductLedgerService productLedgerService) : ControllerBase
{
    /// <summary>?barcode=...&amp;storeId=... : stock and price of one size at the counter. Nothing is saved.</summary>
    [HttpGet(ApiRoutes.Products.PriceCheck)]
    [RequirePermission(PermissionCodes.Products.PriceCheck.View)]
    public async Task<ActionResult<VariantPriceLookupResponse>> CheckPrice([FromQuery] string? barcode, [FromQuery] Guid? storeId, CancellationToken cancellationToken) =>
        await productListingService.GetPriceLookupAsync(barcode, storeId, cancellationToken);

    /// <summary>?storeId=...&amp;barcode=... (or productId)&amp;fromDate=2026-01-01&amp;toDate=2026-01-31&amp;detail=true</summary>
    [HttpGet(ApiRoutes.Products.Ledger)]
    [RequirePermission(PermissionCodes.Products.ProductLedger.View)]
    public async Task<ActionResult<ProductLedgerResponse>> GetLedger([FromQuery] ProductLedgerQuery query, CancellationToken cancellationToken) =>
        await productLedgerService.GetLedgerAsync(query, cancellationToken);

    /// <summary>The text to look for (name, code or barcode) goes in the page query's search text.</summary>
    [HttpGet(ApiRoutes.Products.StockByStore)]
    [RequirePermission(PermissionCodes.Products.ProductsStockWithSizesOnMultipleStores.View)]
    public async Task<ActionResult<PagedResult<ProductStockByStoreRowResponse>>> ListStockByStore([FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        await productListingService.ListStockByStoreAsync(pageQuery.ToPageRequest(), cancellationToken);

    [HttpGet(ApiRoutes.Products.Report)]
    [RequirePermission(PermissionCodes.Products.ProductReport.View)]
    public async Task<ActionResult<PagedResult<ProductReportRowResponse>>> ListProductReport(
        [FromQuery] PageQuery pageQuery, [FromQuery] ProductReportFilter filter, CancellationToken cancellationToken) =>
        await productListingService.ListProductReportAsync(filter, pageQuery.ToPageRequest(), cancellationToken);
}
