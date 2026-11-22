namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>The Account Balance report (docx 8.2): balances of all accounts (or parties) of one type as on a date, with the column totals.</summary>
public sealed record AccountBalanceReportResponse(
    DateOnly AsOfDate,
    Guid? StoreId,
    string AccountTypeFilter,
    string AmountTypeFilter,
    IReadOnlyList<AccountBalanceRowResponse> Rows,
    long TotalDebitAmount,
    long TotalCreditAmount);
