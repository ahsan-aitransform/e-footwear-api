using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.BLL.Inventory.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Inventory;

/// <summary>The stock adjustment, transfer and transfer summary reports (docx 8.20 to 8.22) and Stock Reconciliation. The claim and audit reports are served beside their own screens.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Inventory.Root)]
public sealed class InventoryReportsController(InventoryReportService inventoryReportService) : ControllerBase
{
    [HttpGet(ApiRoutes.Inventory.StockAdjustmentReport)]
    [RequirePermission(PermissionCodes.Reports.StockAdjustmentReport.View)]
    public async Task<ActionResult<StockAdjustmentReportResponse>> GetAdjustmentReport([FromQuery] StockAdjustmentSearchFilter filter, CancellationToken cancellationToken) =>
        Ok(await inventoryReportService.GetAdjustmentReportAsync(filter, cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockTransferReport)]
    [RequirePermission(PermissionCodes.Reports.StockTransferReport.View)]
    public async Task<ActionResult<StockTransferReportResponse>> GetTransferReport([FromQuery] StockTransferReportFilter filter, CancellationToken cancellationToken) =>
        Ok(await inventoryReportService.GetTransferReportAsync(filter, cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockTransferSummaryReport)]
    [RequirePermission(PermissionCodes.Reports.StockTransferSummary.View)]
    public async Task<ActionResult<StockTransferSummaryResponse>> GetTransferSummary(
        [FromQuery] Guid? storeId, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, CancellationToken cancellationToken) =>
        Ok(await inventoryReportService.GetTransferSummaryAsync(storeId, fromDate, toDate, cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockReconciliation)]
    [RequirePermission(PermissionCodes.Inventory.StockReconciliation.View)]
    public async Task<ActionResult<StockReconciliationResponse>> Reconcile([FromQuery] Guid? storeId, CancellationToken cancellationToken) =>
        Ok(await inventoryReportService.ReconcileAsync(storeId, cancellationToken));
}
