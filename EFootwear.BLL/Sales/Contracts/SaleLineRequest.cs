namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// One line of a sale as the cashier entered it. <paramref name="UnitPrice"/> is the price per unit before the line discount, in whole rupees.
/// <paramref name="DiscountTypeCode"/> is NONE, PERCENT (<paramref name="DiscountValue"/> is a percentage) or AMOUNT (whole rupees per unit).
/// The server checks the price against the shelf price and the promotions: a different price or any discount needs its own permission.
/// </summary>
public sealed record SaleLineRequest(Guid VariantId, decimal Quantity, long UnitPrice, string? DiscountTypeCode, decimal? DiscountValue);
