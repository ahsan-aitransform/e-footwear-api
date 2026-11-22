namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>A size that a bulk price change would touch (or touched): the grid of docx 4.7 and 4.8. NewPrice is the value after the change, of the price being changed.</summary>
public sealed record PriceChangeRowResponse(
    Guid ProductId,
    Guid VariantId,
    string ProductCode,
    string ProductName,
    string SizeCode,
    string Barcode,
    string? CategoryName,
    string? BrandName,
    string? GenderName,
    string? SeasonName,
    decimal StockQuantity,
    long PurchasePrice,
    long CurrentSalePrice,
    long CurrentPrice,
    long NewPrice);
