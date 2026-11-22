namespace EFootwear.BLL.Parties.Contracts;

/// <summary>The opening balance of a party in force now: the side (DEBIT = the party owes us, CREDIT = we owe the party), the amount and the date.</summary>
public sealed record PartyOpeningBalanceResponse(string SideCode, long Amount, DateOnly AsOfDate);
