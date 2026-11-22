namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// The balance of one cash or bank account as on a date. <c>KindCode</c> is CASH or BANK (the accounts with that system role, plus the accounts of the
/// cash group 101 and bank group 102). The balance is debit minus credit of the journal lines of the selected store, shown as an amount and a side.
/// </summary>
public sealed record CashAndBankAccountBalanceResponse(
    Guid AccountId,
    string AccountCode,
    string AccountTitle,
    string KindCode,
    Guid StoreId,
    string StoreName,
    long BalanceAmount,
    string BalanceSideCode);
