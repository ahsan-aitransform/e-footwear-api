namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// The Trial Balance (docx 8.3) or Detailed Trial Balance (8.4) as on a date. The totals add the GROUP rows only, so an account is never
/// counted twice in the detailed report. <c>IsBalanced</c> is true when total debits equal total credits (they always do, because every journal
/// entry balances); a false value is a data fault that the screen must show.
/// </summary>
public sealed record TrialBalanceResponse(
    DateOnly AsOfDate,
    Guid? StoreId,
    bool IsDetailed,
    IReadOnlyList<TrialBalanceRowResponse> Rows,
    long TotalDebitAmount,
    long TotalCreditAmount,
    long TotalBalanceDebitAmount,
    long TotalBalanceCreditAmount,
    bool IsBalanced);
