using EFootwear.BLL.Common;

namespace EFootwear.BLL.Staff.Contracts;

/// <summary>
/// Staff Account Detail: one page of lines plus the totals of the whole period. For one staff member the opening balance (everything before the
/// period) and the closing balance are given; for all staff they are zero. All amounts are whole rupees derived from the journal.
/// </summary>
public sealed record StaffLedgerResponse(
    Guid? StaffId,
    long OpeningBalanceAmount,
    PagedResult<StaffLedgerLineResponse> Lines,
    long TotalDebitAmount,
    long TotalCreditAmount)
{
    public long ClosingBalanceAmount => OpeningBalanceAmount + TotalDebitAmount - TotalCreditAmount;
}
