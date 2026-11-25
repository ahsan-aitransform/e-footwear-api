namespace EFootwear.BLL.Contacts.Contracts;

/// <summary>The opening balance of a customer, vendor or staff member in force now: the side (DEBIT = the contact owes us, CREDIT = we owe the contact), the amount and the date.</summary>
public sealed record ContactOpeningBalanceResponse(string SideCode, long Amount, DateOnly AsOfDate);
