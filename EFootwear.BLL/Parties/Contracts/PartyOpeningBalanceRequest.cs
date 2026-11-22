namespace EFootwear.BLL.Parties.Contracts;

/// <summary>
/// Sets the opening balance of a party when it is created or edited (Part 8: "opening balance where applicable"). SideCode is DEBIT
/// (the party owes the company) or CREDIT (the company owes the party); amount 0 clears it. Needs the OPENING_BALANCE permission.
/// </summary>
public sealed record PartyOpeningBalanceRequest(string SideCode, long Amount, DateOnly AsOfDate);
