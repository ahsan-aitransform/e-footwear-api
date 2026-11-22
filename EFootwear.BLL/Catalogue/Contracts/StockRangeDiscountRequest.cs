namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// Change Discounts, docx 4.9: give a percentage discount to the products of a brand whose stock in the store is within a range (default 1 to 2),
/// to clear the last pieces. It becomes one product-discount rule for the store; the pricing engine applies it to each size whose stock is in the range.
/// </summary>
public sealed record StockRangeDiscountRequest(
    Guid StoreId,
    Guid? BrandId,
    decimal MinimumStock,
    decimal MaximumStock,
    decimal DiscountPercent,
    Guid? ClientTransactionId);
