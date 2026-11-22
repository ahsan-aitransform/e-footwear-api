using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Accounting.Services;
using EFootwear.BLL.Common;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Accounting;

/// <summary>
/// Account Ledger, Account Balance, Trial Balance, Detailed Trial Balance and General Journal (docx 8.1 to 8.4, 8.28). Read-only. The report
/// functions of the database check the legacy REPORT_ACCOUNTS code, so every report requires it together with the permission of its screen.
/// Leaving <c>storeId</c> empty means "all stores the caller may see".
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.AccountReports.Root)]
public sealed class AccountReportsController(AccountReportService accountReportService) : ControllerBase
{
    [HttpGet(ApiRoutes.AccountReports.Accounts)]
    [RequirePermission(PermissionCodes.Reports.AccountLedger.View)]
    public async Task<ActionResult<IReadOnlyList<AccountLookupItemResponse>>> SearchAccounts(
        [FromQuery] string? searchText, [FromQuery] string? mainTypeCode, [FromQuery] string? groupCode, CancellationToken cancellationToken) =>
        Ok(await accountReportService.SearchAccountsAsync(searchText, mainTypeCode, groupCode, cancellationToken));

    [HttpGet(ApiRoutes.AccountReports.BalanceTypes)]
    [RequirePermission(PermissionCodes.Reports.AccountBalance.View)]
    public async Task<ActionResult<IReadOnlyList<AccountBalanceTypeOptionResponse>>> ListBalanceTypeOptions(CancellationToken cancellationToken) =>
        Ok(await accountReportService.ListBalanceTypeOptionsAsync(cancellationToken));

    [HttpGet(ApiRoutes.AccountReports.Ledger)]
    [RequirePermission(PermissionCodes.Reports.AccountLedger.View)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Reporting.ReportAccounts)]
    public async Task<ActionResult<AccountLedgerResponse>> GetLedger(
        [FromQuery] Guid? accountId, [FromQuery] string? accountCode, [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate,
        [FromQuery] Guid? storeId, [FromQuery] bool includeDescriptions, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await accountReportService.GetLedgerAsync(accountId, accountCode, fromDate, toDate, storeId, includeDescriptions, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.AccountReports.Balances)]
    [RequirePermission(PermissionCodes.Reports.AccountBalance.View)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Reporting.ReportAccounts)]
    public async Task<ActionResult<AccountBalanceReportResponse>> GetAccountBalances(
        [FromQuery] DateOnly asOfDate, [FromQuery] Guid? storeId, [FromQuery] string accountTypeFilter, [FromQuery] string? amountTypeFilter,
        CancellationToken cancellationToken) =>
        Ok(await accountReportService.GetAccountBalancesAsync(asOfDate, storeId, accountTypeFilter, amountTypeFilter, cancellationToken));

    [HttpGet(ApiRoutes.AccountReports.TrialBalance)]
    [RequirePermission(PermissionCodes.Reports.TrialBalance.View)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Reporting.ReportAccounts)]
    public async Task<ActionResult<TrialBalanceResponse>> GetTrialBalance(
        [FromQuery] DateOnly asOfDate, [FromQuery] Guid? storeId, [FromQuery] bool includeZeroBalances, CancellationToken cancellationToken) =>
        Ok(await accountReportService.GetTrialBalanceAsync(asOfDate, storeId, isDetailed: false, includeZeroBalances, cancellationToken));

    [HttpGet(ApiRoutes.AccountReports.DetailTrialBalance)]
    [RequirePermission(PermissionCodes.Reports.DetailTrialBalance.View)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Reporting.ReportAccounts)]
    public async Task<ActionResult<TrialBalanceResponse>> GetDetailedTrialBalance(
        [FromQuery] DateOnly asOfDate, [FromQuery] Guid? storeId, [FromQuery] bool includeZeroBalances, CancellationToken cancellationToken) =>
        Ok(await accountReportService.GetTrialBalanceAsync(asOfDate, storeId, isDetailed: true, includeZeroBalances, cancellationToken));

    [HttpGet(ApiRoutes.AccountReports.GeneralJournal)]
    [RequirePermission(PermissionCodes.Reports.GeneralJournal.View)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Reporting.ReportAccounts)]
    public async Task<ActionResult<GeneralJournalResponse>> GetGeneralJournal(
        [FromQuery] DateOnly fromDate, [FromQuery] DateOnly toDate, [FromQuery] Guid? storeId, [FromQuery] string? typeCode,
        [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await accountReportService.GetGeneralJournalAsync(fromDate, toDate, storeId, typeCode, pageRequest, cancellationToken));
}
