using EFootwear.BLL.Common;

namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// The ledger of one account for a date range (docx 8.1): opening balance, one page of lines with running balance, and totals over the
/// whole range (not only the page). Opening and closing balances are absolute amounts with their side. All figures come from journal lines.
/// </summary>
public sealed record AccountLedgerResponse(
    AccountLookupItemResponse Account,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? StoreId,
    long OpeningBalanceAmount,
    string OpeningBalanceSideCode,
    long TotalDebitAmount,
    long TotalCreditAmount,
    long ClosingBalanceAmount,
    string ClosingBalanceSideCode,
    PagedResult<AccountLedgerRowResponse> Lines);
