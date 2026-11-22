namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>A row of the grid of docx 4.11: the product in one store with its stock there and the discount it has now.</summary>
public sealed record ProductStoreDiscountRowResponse(
    Guid ProductId,
    string ProductName,
    Guid StoreId,
    string StoreName,
    decimal StockQuantity,
    decimal? DiscountPercent);
