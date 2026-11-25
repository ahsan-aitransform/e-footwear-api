using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Payroll.Contracts;
using EFootwear.BLL.Payroll.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Payroll;

/// <summary>
/// The monthly salary sheet of a store (docx 3.8 New Salary Sheet, 3.9 View Salary Sheet), payroll adjustments and salary payments (Part 16.3).
/// Preparing needs the screen permission and the legacy code PAYROLL_PREPARE; approving and paying need PAYROLL_APPROVE, because the database functions
/// check those codes again. Nothing is paid by approving a sheet: each payment is its own action.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Payroll.Root)]
public sealed class PayrollController(PayrollSheetService payrollSheetService) : ControllerBase
{
    /// <summary>The sheet of a store and month, or 204 when none has been built yet.</summary>
    [HttpGet(ApiRoutes.Payroll.Sheet)]
    [RequirePermission(PermissionCodes.HumanResources.ViewSalarySheet.View)]
    public async Task<ActionResult<PayrollSheetResponse>> GetSheet([FromQuery] Guid storeId, [FromQuery] DateOnly month, CancellationToken cancellationToken)
    {
        var sheet = await payrollSheetService.GetSheetAsync(storeId, month, cancellationToken);
        return sheet is null ? NoContent() : Ok(sheet);
    }

    [HttpPost(ApiRoutes.Payroll.PrepareSheet)]
    [RequirePermission(PermissionCodes.HumanResources.NewSalarySheet.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.ExpensesAndPayroll.PayrollPrepare)]
    public async Task<ActionResult<PayrollSheetResponse>> PrepareSheet(PrepareSalarySheetRequest request, CancellationToken cancellationToken) =>
        Ok(await payrollSheetService.PrepareSheetAsync(request, cancellationToken));

    [HttpPost(ApiRoutes.Payroll.ApproveSheet)]
    [RequirePermission(PermissionCodes.HumanResources.PayrollRuns.Approve)]
    [RequirePermission(PermissionCodes.RestrictedOperations.ExpensesAndPayroll.PayrollApprove)]
    public async Task<ActionResult<PayrollSheetResponse>> ApproveSheet(Guid runId, CancellationToken cancellationToken) =>
        Ok(await payrollSheetService.ApproveSheetAsync(runId, cancellationToken));

    [HttpGet(ApiRoutes.Payroll.Adjustments)]
    [RequirePermission(PermissionCodes.HumanResources.NewSalarySheet.View)]
    public async Task<ActionResult<IReadOnlyList<PayrollAdjustmentResponse>>> ListAdjustments(
        [FromQuery] Guid storeId, [FromQuery] DateOnly month, CancellationToken cancellationToken) =>
        Ok(await payrollSheetService.ListAdjustmentsAsync(storeId, month, cancellationToken));

    [HttpPost(ApiRoutes.Payroll.Adjustments)]
    [RequirePermission(PermissionCodes.HumanResources.NewSalarySheet.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.ExpensesAndPayroll.PayrollPrepare)]
    public async Task<ActionResult<CreatedIdResponse>> CreateAdjustment(CreatePayrollAdjustmentRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await payrollSheetService.CreateAdjustmentAsync(request, cancellationToken));

    [HttpPost(ApiRoutes.Payroll.LinePayments)]
    [RequirePermission(PermissionCodes.HumanResources.SalaryPayments.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.ExpensesAndPayroll.PayrollApprove)]
    public async Task<ActionResult<SalaryPaymentResponse>> PaySalary(Guid lineId, PaySalaryRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await payrollSheetService.PaySalaryAsync(lineId, request, cancellationToken));

    [HttpGet(ApiRoutes.Payroll.LinePayments)]
    [RequirePermission(PermissionCodes.HumanResources.SalaryPayments.View)]
    public async Task<ActionResult<IReadOnlyList<SalaryPaymentResponse>>> ListLinePayments(Guid lineId, CancellationToken cancellationToken) =>
        Ok(await payrollSheetService.ListLinePaymentsAsync(lineId, cancellationToken));

    [HttpGet(ApiRoutes.Payroll.Payments)]
    [RequirePermission(PermissionCodes.HumanResources.SalaryPayments.View)]
    public async Task<ActionResult<PagedResult<SalaryPaymentResponse>>> ListPayments(
        [FromQuery] SalaryPaymentSearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await payrollSheetService.SearchPaymentsAsync(filter, pageRequest, cancellationToken));
}
