using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Payroll.Contracts;
using EFootwear.BLL.Payroll.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Payroll;

/// <summary>The Staff Salary and Salary Outstanding reports (Part 22 additional reports). Each has its own View permission.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Payroll.Root)]
public sealed class PayrollReportsController(PayrollReportService payrollReportService) : ControllerBase
{
    [HttpGet(ApiRoutes.Payroll.StaffSalaryReport)]
    [RequirePermission(PermissionCodes.Reports.StaffSalaryReport.View)]
    public async Task<ActionResult<StaffSalaryReportResponse>> GetStaffSalaryReport([FromQuery] StaffSalaryReportRequest request, CancellationToken cancellationToken) =>
        Ok(await payrollReportService.GetStaffSalaryReportAsync(request, cancellationToken));

    [HttpGet(ApiRoutes.Payroll.SalaryOutstandingReport)]
    [RequirePermission(PermissionCodes.Reports.SalaryOutstandingReport.View)]
    public async Task<ActionResult<SalaryOutstandingReportResponse>> GetSalaryOutstandingReport(
        [FromQuery] SalaryOutstandingReportRequest request, CancellationToken cancellationToken) =>
        Ok(await payrollReportService.GetSalaryOutstandingReportAsync(request, cancellationToken));
}
