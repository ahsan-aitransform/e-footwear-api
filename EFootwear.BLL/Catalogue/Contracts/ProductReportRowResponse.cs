namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// A row of the Product Report (docx 4.15): a product in one store with the prices there and the stock of all its sizes. When the sizes of a
/// product have different prices, the highest purchase price and the highest sale price are shown. SalePrice is the store price (or list price).
/// </summary>
public sealed record ProductReportRowResponse(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    long PurchasePrice,
    long SalePrice,
    string? BrandName,
    string? SeasonName,
    string? ColorName,
    Guid StoreId,
    string StoreName,
    decimal StockQuantity);
