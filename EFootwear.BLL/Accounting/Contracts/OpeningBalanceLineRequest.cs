namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>A line to save: the account or the party (exactly one) and its opening amount on the debit or the credit side. Both zero clears it.</summary>
public sealed record OpeningBalanceLineRequest(Guid? AccountId, Guid? PartyId, long DebitAmount, long CreditAmount);
