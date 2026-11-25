namespace EFootwear.BLL.Contacts.Contracts;

/// <summary>
/// Sets the opening balance of a customer, vendor or staff member when it is created or edited. SideCode is DEBIT (the contact owes the
/// company) or CREDIT (the company owes the contact); amount 0 clears it. Needs the OPENING_BALANCE permission.
/// </summary>
public sealed record ContactOpeningBalanceRequest(string SideCode, long Amount, DateOnly AsOfDate);
