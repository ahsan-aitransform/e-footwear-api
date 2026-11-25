namespace EFootwear.BLL.Vendors.Contracts;

/// <summary>
/// One journal line of a vendor. SourceType says what posted it (SALE, VOUCHER, OPENING ...); DocumentNumber is the voucher or invoice number when the
/// line has one. RunningBalanceAmount is the vendor's balance after this line (debit minus credit, over the whole history).
/// </summary>
public sealed record VendorLedgerLineResponse(
    Guid LineId,
    Guid VendorId,
    string VendorCode,
    string VendorName,
    DateOnly EntryDate,
    string SourceType,
    long? DocumentNumber,
    string? Description,
    long DebitAmount,
    long CreditAmount,
    long RunningBalanceAmount);
