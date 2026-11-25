namespace EFootwear.BLL.Customers.Contracts;

/// <summary>
/// One journal line of a customer. SourceType says what posted it (SALE, VOUCHER, OPENING ...); DocumentNumber is the voucher or invoice number when the
/// line has one. RunningBalanceAmount is the customer's balance after this line (debit minus credit, over the whole history).
/// </summary>
public sealed record CustomerLedgerLineResponse(
    Guid LineId,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    DateOnly EntryDate,
    string SourceType,
    long? DocumentNumber,
    string? Description,
    long DebitAmount,
    long CreditAmount,
    long RunningBalanceAmount);
