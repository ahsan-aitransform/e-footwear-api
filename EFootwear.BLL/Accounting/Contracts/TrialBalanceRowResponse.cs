namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// A row of the trial balance. <c>RowKindCode</c> is GROUP (an account head with the totals of its accounts) or ACCOUNT (one account under
/// its head, only in the detailed report). <c>TotalDebitAmount</c> and <c>TotalCreditAmount</c> are the sums of all journal lines up to the date;
/// <c>BalanceDebitAmount</c> / <c>BalanceCreditAmount</c> show the net balance on its side.
/// </summary>
public sealed record TrialBalanceRowResponse(
    string RowKindCode,
    string GroupCode,
    string GroupTitle,
    Guid? AccountId,
    string? AccountCode,
    string? AccountTitle,
    string MainTypeCode,
    long TotalDebitAmount,
    long TotalCreditAmount,
    long BalanceDebitAmount,
    long BalanceCreditAmount);
