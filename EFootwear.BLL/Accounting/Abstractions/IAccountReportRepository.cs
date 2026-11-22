using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Accounting.Abstractions;

/// <summary>
/// The account reports (docx 8.1 to 8.4, 8.28), read-only. Figures come from the journal through the SQL report functions
/// rpt.account_ledger, rpt.trial_balance, rpt.account_balance and rpt.general_journal, which check the REPORT_ACCOUNTS permission themselves.
/// Totals are taken in separate aggregates so joins never multiply amounts.
/// </summary>
public interface IAccountReportRepository
{
    Task<IReadOnlyList<AccountLookupItemResponse>> SearchAccountsAsync(
        string? searchText, string? mainTypeCode, string? groupCode, int limit, CancellationToken cancellationToken);

    Task<AccountLookupItemResponse?> FindAccountAsync(Guid? accountId, string? accountCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountBalanceTypeOptionResponse>> ListBalanceTypeOptionsAsync(CancellationToken cancellationToken);

    Task<AccountLedgerResponse> GetLedgerAsync(
        AccountLookupItemResponse account, DateOnly fromDate, DateOnly toDate, Guid? storeId, bool includeDescriptions,
        PageRequest pageRequest, CancellationToken cancellationToken);

    Task<AccountBalanceReportResponse> GetAccountBalancesAsync(
        DateOnly asOfDate, Guid? storeId, string accountTypeFilter, string amountTypeFilter, CancellationToken cancellationToken);

    Task<TrialBalanceResponse> GetTrialBalanceAsync(
        DateOnly asOfDate, Guid? storeId, bool isDetailed, bool includeZeroBalances, CancellationToken cancellationToken);

    Task<GeneralJournalResponse> GetGeneralJournalAsync(
        DateOnly fromDate, DateOnly toDate, Guid? storeId, string? typeCode, PageRequest pageRequest, CancellationToken cancellationToken);
}
