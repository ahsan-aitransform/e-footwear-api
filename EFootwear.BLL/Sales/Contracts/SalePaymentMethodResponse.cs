namespace EFootwear.BLL.Sales.Contracts;

/// <summary>A payment method the company has switched on (Settings, Payment Methods), in the order the company chose.</summary>
public sealed record SalePaymentMethodResponse(string Code, string? DisplayNameEnglish, string? DisplayNameUrdu);
