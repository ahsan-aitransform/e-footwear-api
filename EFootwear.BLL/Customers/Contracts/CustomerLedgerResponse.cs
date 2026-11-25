using EFootwear.BLL.Common;

namespace EFootwear.BLL.Customers.Contracts;

/// <summary>
/// Customer Account Detail: one page of lines plus the totals of the whole period. For one customer the opening balance (everything before the
/// period) and the closing balance are given; for all customers they are zero. All amounts are whole rupees derived from the journal.
/// </summary>
public sealed record CustomerLedgerResponse(
    Guid? CustomerId,
    long OpeningBalanceAmount,
    PagedResult<CustomerLedgerLineResponse> Lines,
    long TotalDebitAmount,
    long TotalCreditAmount)
{
    public long ClosingBalanceAmount => OpeningBalanceAmount + TotalDebitAmount - TotalCreditAmount;
}
