using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Common;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;

namespace EFootwear.BLL.Accounting.Services;

/// <summary>
/// Account Ledger, Account Balance, Trial Balance, Detailed Trial Balance and General Journal (docx 8.1 to 8.4, 8.28). Read-only: every figure is
/// derived from journal lines by the database, never from a stored balance. This service validates the filters and the store scope; row-level
/// security limits the data to the caller's stores, and an owner may leave the store empty to see every store ("All Stores").
/// </summary>
public sealed class AccountReportService(IAccountReportRepository accountReportRepository, ICurrentUser currentUser)
{
    public Task<IReadOnlyList<AccountLookupItemResponse>> SearchAccountsAsync(
        string? searchText, string? mainTypeCode, string? groupCode, CancellationToken cancellationToken)
    {
        if (mainTypeCode is not null && AccountMainTypes.FindByCode(mainTypeCode) is null)
            throw new DomainException(ErrorKind.Validation, "That account type does not exist.");
        var cleanedSearchText = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
        return accountReportRepository.SearchAccountsAsync(cleanedSearchText, mainTypeCode, groupCode, AccountReportRules.LookupResultLimit, cancellationToken);
    }

    public Task<IReadOnlyList<AccountBalanceTypeOptionResponse>> ListBalanceTypeOptionsAsync(CancellationToken cancellationToken) =>
        accountReportRepository.ListBalanceTypeOptionsAsync(cancellationToken);

    public async Task<AccountLedgerResponse> GetLedgerAsync(
        Guid? accountId, string? accountCode, DateOnly fromDate, DateOnly toDate, Guid? storeId, bool includeDescriptions,
        PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (accountId is null && string.IsNullOrWhiteSpace(accountCode))
            throw new DomainException(ErrorKind.Validation, "Choose an account or type its account number.");
        EnsureDateRangeIsValid(fromDate, toDate);
        EnsureStoreScope(storeId);

        var account = await accountReportRepository.FindAccountAsync(accountId, accountCode?.Trim(), cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The account was not found.");
        return await accountReportRepository.GetLedgerAsync(account, fromDate, toDate, storeId, includeDescriptions, pageRequest.Normalized(), cancellationToken);
    }

    public Task<AccountBalanceReportResponse> GetAccountBalancesAsync(
        DateOnly asOfDate, Guid? storeId, string accountTypeFilter, string? amountTypeFilter, CancellationToken cancellationToken)
    {
        EnsureStoreScope(storeId);
        AccountReportRules.EnsureAccountTypeFilterIsValid(accountTypeFilter);
        var amountType = string.IsNullOrWhiteSpace(amountTypeFilter) ? BalanceSideCodes.All : amountTypeFilter;
        if (!BalanceSideCodes.IsValidFilter(amountType))
            throw new DomainException(ErrorKind.Validation, "The amount type must be Debit, Credit or All.");
        return accountReportRepository.GetAccountBalancesAsync(asOfDate, storeId, accountTypeFilter, amountType, cancellationToken);
    }

    public Task<TrialBalanceResponse> GetTrialBalanceAsync(
        DateOnly asOfDate, Guid? storeId, bool isDetailed, bool includeZeroBalances, CancellationToken cancellationToken)
    {
        EnsureStoreScope(storeId);
        return accountReportRepository.GetTrialBalanceAsync(asOfDate, storeId, isDetailed, includeZeroBalances, cancellationToken);
    }

    public Task<GeneralJournalResponse> GetGeneralJournalAsync(
        DateOnly fromDate, DateOnly toDate, Guid? storeId, string? typeCode, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        EnsureDateRangeIsValid(fromDate, toDate);
        EnsureStoreScope(storeId);
        string? cleanedTypeCode = null;
        if (!string.IsNullOrWhiteSpace(typeCode))
        {
            cleanedTypeCode = typeCode.Trim().ToUpperInvariant();
            if (cleanedTypeCode.Length > AccountReportRules.TypeCodeMaximumLength || !cleanedTypeCode.All(character => char.IsAsciiLetterUpper(character) || character == '_'))
                throw new DomainException(ErrorKind.Validation, "That entry type is not valid.");
        }
        return accountReportRepository.GetGeneralJournalAsync(fromDate, toDate, storeId, cleanedTypeCode, pageRequest.Normalized(), cancellationToken);
    }

    private static void EnsureDateRangeIsValid(DateOnly fromDate, DateOnly toDate)
    {
        if (fromDate > toDate)
            throw new DomainException(ErrorKind.Validation, "The To date cannot be before the From date.");
    }

    private void EnsureStoreScope(Guid? storeId)
    {
        if (storeId is { } id && !currentUser.CanAccessStore(id))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
    }
}
