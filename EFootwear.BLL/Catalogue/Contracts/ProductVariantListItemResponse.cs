namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// One row of View Products / Search Product: one size of a product (docx 4.2: "one row per size"). Money is whole rupees.
/// SalePrice is the store price (or the company list price); DiscountAmount and DiscountPercent come from the pricing engine
/// (promo.effective_prices) for the selected store; NetPrice = SalePrice - DiscountAmount; MarginPercent is on the net price (docx 4.2) and
/// MarkupPercent on the purchase price (docx 4.10 calls it Profit %): two different figures, labelled separately on the screens.
/// </summary>
public sealed record ProductVariantListItemResponse(
    Guid ProductId,
    Guid VariantId,
    string ProductCode,
    string Barcode,
    string ProductName,
    string? ProductNameUrdu,
    string? UnitName,
    string SizeCode,
    string? ColorName,
    decimal StockQuantity,
    long PurchasePrice,
    long SalePrice,
    decimal? DiscountPercent,
    long DiscountAmount,
    long NetPrice,
    decimal? MarginPercent,
    decimal? MarkupPercent,
    string DepartmentName,
    string CategoryName,
    string? BrandName,
    string? GenderName,
    string? SeasonName,
    string Status);
