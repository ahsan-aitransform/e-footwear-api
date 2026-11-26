namespace EFootwear.BLL.Sales.Contracts;

/// <summary>One item of an earlier invoice of a customer.</summary>
public sealed record SaleCustomerPurchaseLineResponse(
    Guid LineId,
    string ProductName,
    string SizeCode,
    string? ColorName,
    decimal Quantity,
    long UnitPrice,
    long NetAmount);
