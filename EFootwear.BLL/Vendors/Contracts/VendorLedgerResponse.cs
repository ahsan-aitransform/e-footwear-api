using EFootwear.BLL.Common;

namespace EFootwear.BLL.Vendors.Contracts;

/// <summary>
/// Vendor Account Detail: one page of lines plus the totals of the whole period. For one vendor the opening balance (everything before the
/// period) and the closing balance are given; for all vendors they are zero. All amounts are whole rupees derived from the journal.
/// </summary>
public sealed record VendorLedgerResponse(
    Guid? VendorId,
    long OpeningBalanceAmount,
    PagedResult<VendorLedgerLineResponse> Lines,
    long TotalDebitAmount,
    long TotalCreditAmount)
{
    public long ClosingBalanceAmount => OpeningBalanceAmount + TotalDebitAmount - TotalCreditAmount;
}
