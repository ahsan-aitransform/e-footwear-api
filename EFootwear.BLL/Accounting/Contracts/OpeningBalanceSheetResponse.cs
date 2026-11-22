namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// The opening balances of one store (or of the head office, which also lists every party). The totals are those of the saved
/// balances. The difference is carried by the company's Opening balance equity account, because every opening entry is balanced
/// against it (see migration 0200).
/// </summary>
public sealed record OpeningBalanceSheetResponse(
    Guid StoreId,
    IReadOnlyList<OpeningBalanceRowResponse> Rows,
    long TotalDebitAmount,
    long TotalCreditAmount)
{
    public long DifferenceAmount => TotalDebitAmount - TotalCreditAmount;
}
