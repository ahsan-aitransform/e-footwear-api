using EFootwear.BLL.Common;

namespace EFootwear.BLL.Parties.Contracts;

/// <summary>
/// Party Account Detail: one page of lines plus the totals of the whole period (the two total boxes of docx 3.4). For one party the
/// opening balance (everything before the period) and the closing balance are given; for all parties they are zero.
/// All amounts are whole rupees derived from the journal.
/// </summary>
public sealed record PartyLedgerResponse(
    Guid? PartyId,
    long OpeningBalanceAmount,
    PagedResult<PartyLedgerLineResponse> Lines,
    long TotalDebitAmount,
    long TotalCreditAmount)
{
    public long ClosingBalanceAmount => OpeningBalanceAmount + TotalDebitAmount - TotalCreditAmount;
}
