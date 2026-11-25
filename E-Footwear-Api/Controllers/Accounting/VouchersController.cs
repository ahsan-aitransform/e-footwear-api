using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Accounting.Services;
using EFootwear.BLL.Common;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Accounting;

/// <summary>
/// Payment, Receipt, Expense and Journal vouchers, View Voucher and the Voucher Report (docx 6.1 to 6.6). Each entry screen has its own routes and its
/// own permission, because the screens are granted separately (Part 6). Posting also needs the legacy VOUCHER_POST code and voiding the legacy
/// VOUCHER_DELETE code, because the database functions check them again. The actions are written out per voucher type on purpose: the convention test
/// reads the attributes of every declared action.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Vouchers.Root)]
public sealed class VouchersController(VoucherService voucherService) : ControllerBase
{
    // ------------------------------------------------------------ Payment (6.1)

    [HttpGet(ApiRoutes.Vouchers.PaymentEntryContext)]
    [RequirePermission(PermissionCodes.Voucher.PaymentVoucher.Add)]
    public async Task<ActionResult<VoucherEntryContextResponse>> GetPaymentEntryContext([FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await voucherService.GetEntryContextAsync(VoucherTypeCodes.Payment, storeId, cancellationToken));

    [HttpGet(ApiRoutes.Vouchers.PaymentTargets)]
    [RequirePermission(PermissionCodes.Voucher.PaymentVoucher.Add)]
    public async Task<ActionResult<IReadOnlyList<VoucherTargetOptionResponse>>> SearchPaymentTargets(
        [FromQuery] string kindCode, [FromQuery] string? searchText, [FromQuery] string? groupCode,
        CancellationToken cancellationToken) =>
        Ok(await voucherService.SearchTargetsAsync(kindCode, searchText, groupCode, cancellationToken));

    [HttpPost(ApiRoutes.Vouchers.Payment)]
    [RequirePermission(PermissionCodes.Voucher.PaymentVoucher.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Accounting.VoucherPost)]
    public async Task<ActionResult<VoucherResponse>> PostPaymentVoucher(PostVoucherRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await voucherService.PostAsync(VoucherTypeCodes.Payment, request, cancellationToken));

    // ------------------------------------------------------------ Receipt (6.2)

    [HttpGet(ApiRoutes.Vouchers.ReceiptEntryContext)]
    [RequirePermission(PermissionCodes.Voucher.ReceiptVoucher.Add)]
    public async Task<ActionResult<VoucherEntryContextResponse>> GetReceiptEntryContext([FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await voucherService.GetEntryContextAsync(VoucherTypeCodes.Receipt, storeId, cancellationToken));

    [HttpGet(ApiRoutes.Vouchers.ReceiptTargets)]
    [RequirePermission(PermissionCodes.Voucher.ReceiptVoucher.Add)]
    public async Task<ActionResult<IReadOnlyList<VoucherTargetOptionResponse>>> SearchReceiptTargets(
        [FromQuery] string kindCode, [FromQuery] string? searchText, [FromQuery] string? groupCode,
        CancellationToken cancellationToken) =>
        Ok(await voucherService.SearchTargetsAsync(kindCode, searchText, groupCode, cancellationToken));

    [HttpPost(ApiRoutes.Vouchers.Receipt)]
    [RequirePermission(PermissionCodes.Voucher.ReceiptVoucher.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Accounting.VoucherPost)]
    public async Task<ActionResult<VoucherResponse>> PostReceiptVoucher(PostVoucherRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await voucherService.PostAsync(VoucherTypeCodes.Receipt, request, cancellationToken));

    // ------------------------------------------------------------ Expense (6.3)

    [HttpGet(ApiRoutes.Vouchers.ExpenseEntryContext)]
    [RequirePermission(PermissionCodes.Voucher.ExpenseVoucher.Add)]
    public async Task<ActionResult<VoucherEntryContextResponse>> GetExpenseEntryContext([FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await voucherService.GetEntryContextAsync(VoucherTypeCodes.Expense, storeId, cancellationToken));

    [HttpGet(ApiRoutes.Vouchers.ExpenseTargets)]
    [RequirePermission(PermissionCodes.Voucher.ExpenseVoucher.Add)]
    public async Task<ActionResult<IReadOnlyList<VoucherTargetOptionResponse>>> SearchExpenseTargets(
        [FromQuery] string kindCode, [FromQuery] string? searchText, [FromQuery] string? groupCode,
        CancellationToken cancellationToken) =>
        Ok(await voucherService.SearchTargetsAsync(kindCode, searchText, groupCode, cancellationToken));

    [HttpPost(ApiRoutes.Vouchers.Expense)]
    [RequirePermission(PermissionCodes.Voucher.ExpenseVoucher.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Accounting.VoucherPost)]
    public async Task<ActionResult<VoucherResponse>> PostExpenseVoucher(PostVoucherRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await voucherService.PostAsync(VoucherTypeCodes.Expense, request, cancellationToken));

    // ------------------------------------------------------------ Journal (6.4)

    [HttpGet(ApiRoutes.Vouchers.JournalEntryContext)]
    [RequirePermission(PermissionCodes.Voucher.JournalVoucher.Add)]
    public async Task<ActionResult<VoucherEntryContextResponse>> GetJournalEntryContext([FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await voucherService.GetEntryContextAsync(VoucherTypeCodes.Journal, storeId, cancellationToken));

    [HttpGet(ApiRoutes.Vouchers.JournalTargets)]
    [RequirePermission(PermissionCodes.Voucher.JournalVoucher.Add)]
    public async Task<ActionResult<IReadOnlyList<VoucherTargetOptionResponse>>> SearchJournalTargets(
        [FromQuery] string kindCode, [FromQuery] string? searchText, [FromQuery] string? groupCode,
        CancellationToken cancellationToken) =>
        Ok(await voucherService.SearchTargetsAsync(kindCode, searchText, groupCode, cancellationToken));

    [HttpPost(ApiRoutes.Vouchers.Journal)]
    [RequirePermission(PermissionCodes.Voucher.JournalVoucher.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Accounting.VoucherPost)]
    public async Task<ActionResult<VoucherResponse>> PostJournalVoucher(PostVoucherRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await voucherService.PostAsync(VoucherTypeCodes.Journal, request, cancellationToken));

    // ------------------------------------------------------ View Voucher (6.5)

    [HttpGet]
    [RequirePermission(PermissionCodes.Voucher.ViewVoucher.View)]
    public async Task<ActionResult<PagedResult<VoucherListItemResponse>>> Search(
        [FromQuery] VoucherSearchFilter filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await voucherService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.Vouchers.ByNumber)]
    [RequirePermission(PermissionCodes.Voucher.ViewVoucher.View)]
    public async Task<ActionResult<VoucherResponse>> GetByNumber(
        [FromQuery] string voucherTypeCode, [FromQuery] long voucherNumber, [FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await voucherService.GetByNumberAsync(voucherTypeCode, voucherNumber, storeId, cancellationToken));

    [HttpGet(ApiRoutes.Vouchers.VoucherById)]
    [RequirePermission(PermissionCodes.Voucher.ViewVoucher.View)]
    public async Task<ActionResult<VoucherResponse>> GetById(Guid voucherId, CancellationToken cancellationToken) =>
        Ok(await voucherService.GetByIdAsync(voucherId, cancellationToken));

    [HttpPut(ApiRoutes.Vouchers.Description)]
    [RequirePermission(PermissionCodes.Voucher.ViewVoucher.Edit)]
    public async Task<ActionResult<VoucherResponse>> UpdateDescription(
        Guid voucherId, UpdateVoucherDescriptionRequest request, CancellationToken cancellationToken) =>
        Ok(await voucherService.UpdateDescriptionAsync(voucherId, request, cancellationToken));

    [HttpPost(ApiRoutes.Vouchers.Void)]
    [RequirePermission(PermissionCodes.Voucher.ViewVoucher.Delete)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Accounting.VoucherDelete)]
    public async Task<ActionResult<VoucherResponse>> Void(Guid voucherId, VoidVoucherRequest request, CancellationToken cancellationToken) =>
        Ok(await voucherService.VoidAsync(voucherId, request, cancellationToken));

    // ---------------------------------------------------- Voucher Report (6.6)

    [HttpGet(ApiRoutes.Vouchers.Report)]
    [RequirePermission(PermissionCodes.Voucher.VoucherReport.View)]
    public async Task<ActionResult<VoucherReportResponse>> BuildReport([FromQuery] VoucherSearchFilter filter, CancellationToken cancellationToken) =>
        Ok(await voucherService.BuildReportAsync(filter, cancellationToken));
}
