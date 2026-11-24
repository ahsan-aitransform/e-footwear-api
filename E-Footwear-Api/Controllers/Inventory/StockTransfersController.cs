using E_Footwear_Api.Authorization;
using E_Footwear_Api.Binding;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.BLL.Inventory.Services;
using EFootwear.Domain.Inventory;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Inventory;

/// <summary>
/// Transfers between stores with scan-based receiving (Part 19; docx 5.9, 5.10): the sending side (draft, send scans, dispatch, cancel), the multi-store form,
/// the receiving side and the decision of missing pieces. The functions also check TRANSFER_CREATE / TRANSFER_RECEIVE / TRANSFER_LOSS_DECIDE.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Inventory.Root)]
public sealed class StockTransfersController(StockTransferService stockTransferService, InventoryLookupService inventoryLookupService) : ControllerBase
{
    // ---- sending side

    [HttpGet(ApiRoutes.Inventory.StockTransfers)]
    [RequirePermission(PermissionCodes.Invoice.StockTransfer.View)]
    public async Task<ActionResult<PagedResult<StockTransferListItemResponse>>> Search(
        [FromQuery] StockTransferSearchFilter filter, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.SearchAsync(filter with { OnlySentTransfers = false }, pageQuery.ToPageRequest(), cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockTransferById)]
    [RequirePermission(PermissionCodes.Invoice.StockTransfer.View)]
    public async Task<ActionResult<StockTransferResponse>> Get(Guid transferId, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.GetAsync(transferId, cancellationToken));

    [HttpPost(ApiRoutes.Inventory.StockTransfers)]
    [RequirePermission(PermissionCodes.Invoice.StockTransfer.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.TransferCreate)]
    public async Task<ActionResult<StockTransferResponse>> Create(CreateStockTransferRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await stockTransferService.CreateAsync(request, cancellationToken));

    [HttpPost(ApiRoutes.Inventory.StockTransferSendScans)]
    [RequirePermission(PermissionCodes.Invoice.StockTransfer.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.TransferCreate)]
    public async Task<ActionResult<TransferScanResultResponse>> RecordSendScan(Guid transferId, RecordTransferScanRequest request, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.RecordScanAsync(transferId, TransferScanPhaseCodes.Send, request, cancellationToken));

    [HttpPost(ApiRoutes.Inventory.StockTransferDispatch)]
    [RequirePermission(PermissionCodes.Invoice.StockTransfer.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.TransferCreate)]
    public async Task<ActionResult<StockTransferResponse>> Dispatch(Guid transferId, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.DispatchAsync(transferId, cancellationToken));

    [HttpPost(ApiRoutes.Inventory.StockTransferCancel)]
    [RequirePermission(PermissionCodes.Invoice.StockTransfer.Delete)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.TransferCreate)]
    public async Task<ActionResult<StockTransferResponse>> Cancel(Guid transferId, CancelStockTransferRequest request, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.CancelAsync(transferId, request, cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockTransferStores)]
    [RequirePermission(PermissionCodes.Invoice.StockTransfer.View)]
    public async Task<ActionResult<IReadOnlyList<InventoryStoreResponse>>> ListStores(CancellationToken cancellationToken) =>
        Ok(await inventoryLookupService.ListStoresAsync(cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockTransferItemByBarcode)]
    [RequirePermission(PermissionCodes.Invoice.StockTransfer.View)]
    public async Task<ActionResult<InventoryItemResponse>> FindItemByBarcode([FromQuery] Guid storeId, [FromQuery] string? barcode, CancellationToken cancellationToken) =>
        Ok(await inventoryLookupService.FindItemByBarcodeAsync(storeId, barcode, cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockTransferItemSearch)]
    [RequirePermission(PermissionCodes.Invoice.StockTransfer.View)]
    public async Task<ActionResult<IReadOnlyList<InventoryItemResponse>>> SearchItems([FromQuery] Guid storeId, [FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await inventoryLookupService.SearchItemsAsync(storeId, searchText, cancellationToken));

    /// <summary>Stock Transfer Multi (docx 5.10): one sending store, several receiving stores.</summary>
    [HttpPost(ApiRoutes.Inventory.StockTransferMulti)]
    [RequirePermission(PermissionCodes.Invoice.StockTransferMulti.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.TransferCreate)]
    public async Task<ActionResult<CreateMultiStockTransferResponse>> CreateMulti(CreateMultiStockTransferRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await stockTransferService.CreateMultiAsync(request, cancellationToken));

    // ---- receiving side

    [HttpGet(ApiRoutes.Inventory.StockTransferReceiving)]
    [RequirePermission(PermissionCodes.Inventory.StockTransferReceiving.View)]
    public async Task<ActionResult<PagedResult<StockTransferListItemResponse>>> SearchIncoming(
        [FromQuery] Guid storeId, [FromQuery] string? statusCode, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.SearchIncomingAsync(storeId, statusCode, pageQuery.ToPageRequest(), cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockTransferReceivingById)]
    [RequirePermission(PermissionCodes.Inventory.StockTransferReceiving.View)]
    public async Task<ActionResult<StockTransferResponse>> GetIncoming(Guid transferId, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.GetAsync(transferId, cancellationToken));

    [HttpPost(ApiRoutes.Inventory.StockTransferReceivingScans)]
    [RequirePermission(PermissionCodes.Inventory.StockTransferReceiving.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.TransferReceive)]
    public async Task<ActionResult<TransferScanResultResponse>> RecordReceiveScan(Guid transferId, RecordTransferScanRequest request, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.RecordScanAsync(transferId, TransferScanPhaseCodes.Receive, request, cancellationToken));

    [HttpPost(ApiRoutes.Inventory.StockTransferReceivingComplete)]
    [RequirePermission(PermissionCodes.Inventory.StockTransferReceiving.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.TransferReceive)]
    public async Task<ActionResult<StockTransferResponse>> CompleteReceiving(Guid transferId, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.CompleteReceivingAsync(transferId, cancellationToken));

    // ---- missing pieces

    [HttpGet(ApiRoutes.Inventory.TransferDiscrepancies)]
    [RequirePermission(PermissionCodes.Inventory.TransferDiscrepancies.View)]
    public async Task<ActionResult<PagedResult<TransferLossCaseResponse>>> SearchLossCases(
        [FromQuery] string? statusCode, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.SearchLossCasesAsync(statusCode, pageQuery.ToPageRequest(), cancellationToken));

    [HttpPost(ApiRoutes.Inventory.TransferDiscrepancyDecide)]
    [RequirePermission(PermissionCodes.Inventory.TransferDiscrepancies.Approve)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.TransferLossDecide)]
    public async Task<ActionResult<TransferLossCaseResponse>> DecideLoss(Guid lossCaseId, DecideTransferLossRequest request, CancellationToken cancellationToken) =>
        Ok(await stockTransferService.DecideLossAsync(lossCaseId, request, cancellationToken));
}
