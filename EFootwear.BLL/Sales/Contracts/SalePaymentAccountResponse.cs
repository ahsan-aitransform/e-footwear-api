namespace EFootwear.BLL.Sales.Contracts;

/// <summary>A cash or bank account a payment can be kept in. <paramref name="StoreId"/> is the store the account belongs to (the nil id for a company-wide account).</summary>
public sealed record SalePaymentAccountResponse(Guid AccountId, string Code, string Title, string KindCode, Guid StoreId);
