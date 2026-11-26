using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Commissions.Contracts;
using EFootwear.BLL.Commissions.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Commissions;

/// <summary>The Commission Report (docx 8.14, summary and detail) and the Commission Accrual and Payment report (Part 22). Each report has its own View permission.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Commissions.Root)]
public sealed class CommissionReportsController(CommissionReportService commissionReportService) : ControllerBase
{
    [HttpGet(ApiRoutes.Commissions.CommissionReportSummary)]
    [RequirePermission(PermissionCodes.Reports.CommissionReport.View)]
    public async Task<ActionResult<CommissionSummaryReportResponse>> GetSummary([FromQuery] CommissionReportRequest request, CancellationToken cancellationToken) =>
        Ok(await commissionReportService.GetSummaryAsync(request, cancellationToken));

    [HttpGet(ApiRoutes.Commissions.CommissionReportDetail)]
    [RequirePermission(PermissionCodes.Reports.CommissionReport.View)]
    public async Task<ActionResult<CommissionDetailReportResponse>> GetDetail([FromQuery] CommissionReportRequest request, CancellationToken cancellationToken) =>
        Ok(await commissionReportService.GetDetailAsync(request, cancellationToken));

    [HttpGet(ApiRoutes.Commissions.AccrualAndPaymentReport)]
    [RequirePermission(PermissionCodes.Reports.CommissionAccrualAndPaymentReport.View)]
    public async Task<ActionResult<CommissionSummaryReportResponse>> GetAccrualAndPayment([FromQuery] CommissionReportRequest request, CancellationToken cancellationToken) =>
        Ok(await commissionReportService.GetSummaryAsync(request, cancellationToken));
}
