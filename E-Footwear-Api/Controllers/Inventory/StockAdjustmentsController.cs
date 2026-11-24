using E_Footwear_Api.Authorization;
using E_Footwear_Api.Binding;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Common.Spreadsheet;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.BLL.Inventory.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Inventory;

/// <summary>Stock Adjustment (docx 5.7), the non-zero stock list with its count update (docx 5.8) and the void of an adjustment. Saving also needs the legacy STOCK_ADJUST code, which the database function checks again.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Inventory.Root)]
public sealed class StockAdjustmentsController(
    StockAdjustmentService stockAdjustmentService,
    StockCountFileService stockCountFileService,
    InventoryLookupService inventoryLookupService) : ControllerBase
{
    private const long UploadSizeLimitBytes = SpreadsheetReader.MaximumFileBytes + 64 * 1024;

    [HttpGet(ApiRoutes.Inventory.StockAdjustments)]
    [RequirePermission(PermissionCodes.Invoice.StockAdjustment.View)]
    public async Task<ActionResult<PagedResult<StockAdjustmentListItemResponse>>> Search(
        [FromQuery] StockAdjustmentSearchFilter filter, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await stockAdjustmentService.SearchAsync(filter, pageQuery.ToPageRequest(), cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockAdjustmentById)]
    [RequirePermission(PermissionCodes.Invoice.StockAdjustment.View)]
    public async Task<ActionResult<StockAdjustmentResponse>> Get(Guid adjustmentId, CancellationToken cancellationToken) =>
        Ok(await stockAdjustmentService.GetAsync(adjustmentId, cancellationToken));

    [HttpPost(ApiRoutes.Inventory.StockAdjustments)]
    [RequirePermission(PermissionCodes.Invoice.StockAdjustment.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.StockAdjust)]
    public async Task<ActionResult<StockAdjustmentResponse>> Create(CreateStockAdjustmentRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await stockAdjustmentService.CreateAsync(request, cancellationToken));

    [HttpPost(ApiRoutes.Inventory.StockAdjustmentVoid)]
    [RequirePermission(PermissionCodes.Invoice.StockAdjustment.Delete)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.StockAdjust)]
    public async Task<ActionResult<VoidStockAdjustmentResponse>> Void(Guid adjustmentId, VoidStockAdjustmentRequest request, CancellationToken cancellationToken) =>
        Ok(await stockAdjustmentService.VoidAsync(adjustmentId, request, cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockAdjustmentItemByBarcode)]
    [RequirePermission(PermissionCodes.Invoice.StockAdjustment.View)]
    public async Task<ActionResult<InventoryItemResponse>> FindItemByBarcode([FromQuery] Guid storeId, [FromQuery] string? barcode, CancellationToken cancellationToken) =>
        Ok(await inventoryLookupService.FindItemByBarcodeAsync(storeId, barcode, cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockAdjustmentItemSearch)]
    [RequirePermission(PermissionCodes.Invoice.StockAdjustment.View)]
    public async Task<ActionResult<IReadOnlyList<InventoryItemResponse>>> SearchItems([FromQuery] Guid storeId, [FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await inventoryLookupService.SearchItemsAsync(storeId, searchText, cancellationToken));

    // ---- non-zero stock list (docx 5.8)

    [HttpGet(ApiRoutes.Inventory.NonZeroStock)]
    [RequirePermission(PermissionCodes.Invoice.StockAdjustmentNonZeroStock.View)]
    public async Task<ActionResult<PagedResult<NonZeroStockRowResponse>>> ListNonZeroStock(
        [FromQuery] Guid storeId, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await stockAdjustmentService.ListNonZeroStockAsync(storeId, pageQuery.ToPageRequest(), cancellationToken));

    [HttpPost(ApiRoutes.Inventory.NonZeroStockCountUpdates)]
    [RequirePermission(PermissionCodes.Invoice.StockAdjustmentNonZeroStock.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.StockAdjust)]
    public async Task<ActionResult<StockAdjustmentResponse>> CreateCountUpdate(CreateStockCountUpdateRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await stockAdjustmentService.CreateCountUpdateAsync(request, cancellationToken));

    /// <summary>Reads an .xlsx or .csv count file (a Barcode column and a Count column) and checks every row against the store. Nothing is saved.</summary>
    [HttpPost(ApiRoutes.Inventory.NonZeroStockCountFilePreview)]
    [RequirePermission(PermissionCodes.Invoice.StockAdjustmentNonZeroStock.Edit)]
    [RequestSizeLimit(UploadSizeLimitBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<StockCountFilePreviewResponse>> PreviewCountFile([FromQuery] Guid storeId, IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return Ok(await stockCountFileService.PreviewAsync(storeId, content, file.FileName, cancellationToken));
    }
}
