namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>A product the stock-range discount (docx 4.9) would cover: at least one of its sizes has stock in the range in the selected store.</summary>
public sealed record StockRangeDiscountRowResponse(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string? BrandName,
    string? GenderName,
    string? SeasonName,
    string? LocationName,
    decimal StockQuantity,
    long SalePrice,
    decimal? DiscountPercent,
    long DiscountAmount);
