namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>A customer's outstanding balance for the read-only "Balance" field of the Promise screen: positive means the customer owes the company. Derived from the journal.</summary>
public sealed record CustomerBalanceResponse(Guid CustomerPartyId, string CustomerName, long BalanceAmount);
