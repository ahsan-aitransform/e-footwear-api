namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>Cash and bank balances as on a date. The totals add the signed balances (debit positive), so a bank overdraft reduces the cash position.</summary>
public sealed record CashAndBankBalancesResponse(
    DateOnly AsOfDate,
    Guid? StoreId,
    IReadOnlyList<CashAndBankAccountBalanceResponse> Accounts,
    long TotalCashAmount,
    long TotalBankAmount);
