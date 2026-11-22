namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// One line of the Opening Balance screen (docx 2.18 grid: Code, Name, Area, Type, Amount). Either an account or a party:
/// exactly one of <paramref name="AccountId"/> and <paramref name="PartyId"/> is set. The amounts are the opening balance
/// in force now (zero when none was entered); only one of the two is non-zero.
/// </summary>
public sealed record OpeningBalanceRowResponse(
    Guid? AccountId,
    Guid? PartyId,
    string Code,
    string Name,
    string? AreaName,
    long DebitAmount,
    long CreditAmount,
    DateOnly? EntryDate);
