namespace EFootwear.BLL.Contacts.Contracts;

/// <summary>One journal line of a customer, vendor or staff member, with the running balance over the whole history of that contact.</summary>
public sealed record ContactLedgerLine(
    Guid LineId,
    Guid ContactId,
    string ContactCode,
    string ContactName,
    DateOnly EntryDate,
    string SourceType,
    long? DocumentNumber,
    string? Description,
    long DebitAmount,
    long CreditAmount,
    long RunningBalanceAmount);

/// <summary>
/// What the shared ledger query returns for one kind of contact: one page of lines, the number of lines of the whole period and the totals
/// of the period. The opening balance is only given when one contact and a From date are chosen. Each feature shapes it into its own response.
/// </summary>
public sealed record ContactLedgerData(
    long OpeningBalanceAmount,
    IReadOnlyList<ContactLedgerLine> Lines,
    long LineCount,
    long TotalDebitAmount,
    long TotalCreditAmount);
