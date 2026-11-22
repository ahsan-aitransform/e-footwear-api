namespace EFootwear.BLL.Parties.Contracts;

/// <summary>
/// One journal line of a party (docx 3.4 grid: Id, Title, Date, Type, V #, Description, Debit, Credit). SourceType says what posted
/// it (SALE, PURCHASE, VOUCHER, OPENING ...); DocumentNumber is the voucher or invoice number when the line has one.
/// RunningBalanceAmount is the party's balance after this line (debit minus credit, over the party's whole history).
/// </summary>
public sealed record PartyLedgerLineResponse(
    Guid LineId,
    Guid PartyId,
    string PartyCode,
    string PartyName,
    DateOnly EntryDate,
    string SourceType,
    long? DocumentNumber,
    string? Description,
    long DebitAmount,
    long CreditAmount,
    long RunningBalanceAmount);
