namespace EFootwear.BLL.Sales.Contracts;

/// <summary>A customer for the picker. <paramref name="BalanceAmount"/> above zero is what the customer owes the shop; below zero is credit the shop owes.</summary>
public sealed record SaleCustomerLookupResponse(Guid PartyId, string Code, string Name, string? PhoneNumber, long BalanceAmount, long? CreditLimitAmount);
