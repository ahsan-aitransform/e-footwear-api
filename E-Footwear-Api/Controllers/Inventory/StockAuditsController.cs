using E_Footwear_Api.Authorization;
using E_Footwear_Api.Binding;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.BLL.Inventory.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Inventory;

/// <summary>Stock Audit (docx 5.12) and the Stock Audit Report (docx 8.24). The functions also check the legacy STOCK_AUDIT code.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Inventory.Root)]
public sealed class StockAuditsController(StockAuditService stockAuditService) : ControllerBase
{
    [HttpGet(ApiRoutes.Inventory.StockAudits)]
    [RequirePermission(PermissionCodes.Invoice.StockAudit.View)]
    public async Task<ActionResult<PagedResult<StockAuditListItemResponse>>> Search(
        [FromQuery] StockAuditSearchFilter filter, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await stockAuditService.SearchAsync(filter, pageQuery.ToPageRequest(), cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockAuditById)]
    [RequirePermission(PermissionCodes.Invoice.StockAudit.View)]
    public async Task<ActionResult<StockAuditResponse>> Get(Guid auditId, [FromQuery] bool includeUnscannedItems, CancellationToken cancellationToken) =>
        Ok(await stockAuditService.GetAsync(auditId, includeUnscannedItems, cancellationToken));

    /// <summary>The same comparison as the audit screen, under the report's own permission.</summary>
    [HttpGet(ApiRoutes.Inventory.StockAuditReport + "/{auditId:guid}")]
    [RequirePermission(PermissionCodes.Reports.StockAuditReport.View)]
    public async Task<ActionResult<StockAuditResponse>> GetReport(Guid auditId, [FromQuery] bool includeUnscannedItems, CancellationToken cancellationToken) =>
        Ok(await stockAuditService.GetAsync(auditId, includeUnscannedItems, cancellationToken));

    [HttpGet(ApiRoutes.Inventory.StockAuditReport)]
    [RequirePermission(PermissionCodes.Reports.StockAuditReport.View)]
    public async Task<ActionResult<PagedResult<StockAuditListItemResponse>>> SearchForReport(
        [FromQuery] StockAuditSearchFilter filter, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        Ok(await stockAuditService.SearchAsync(filter, pageQuery.ToPageRequest(), cancellationToken));

    [HttpPost(ApiRoutes.Inventory.StockAudits)]
    [RequirePermission(PermissionCodes.Invoice.StockAudit.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.StockAudit)]
    public async Task<ActionResult<StockAuditResponse>> Create(CreateStockAuditRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await stockAuditService.CreateAsync(request, cancellationToken));

    [HttpPost(ApiRoutes.Inventory.StockAuditScans)]
    [RequirePermission(PermissionCodes.Invoice.StockAudit.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.StockAudit)]
    public async Task<ActionResult<StockAuditScanResultResponse>> RecordScan(Guid auditId, RecordStockAuditScanRequest request, CancellationToken cancellationToken) =>
        Ok(await stockAuditService.RecordScanAsync(auditId, request, cancellationToken));

    /// <summary>"Adjust Stock": posts the differences of the count.</summary>
    [HttpPost(ApiRoutes.Inventory.StockAuditPost)]
    [RequirePermission(PermissionCodes.Invoice.StockAudit.Approve)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.StockAudit)]
    public async Task<ActionResult<StockAuditResponse>> Post(Guid auditId, PostStockAuditRequest request, CancellationToken cancellationToken) =>
        Ok(await stockAuditService.PostAsync(auditId, request, cancellationToken));

    [HttpDelete(ApiRoutes.Inventory.StockAuditById)]
    [RequirePermission(PermissionCodes.Invoice.StockAudit.Delete)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Stock.StockAudit)]
    public async Task<IActionResult> Cancel(Guid auditId, CancellationToken cancellationToken)
    {
        await stockAuditService.CancelAsync(auditId, cancellationToken);
        return NoContent();
    }
}
