namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// The answer to Check Price (docx 4.5) for one barcode. ListPrice is the company price, StorePrice the price of the selected store
/// (the same as the list price when the store has none), EffectiveSalePrice what a customer pays after the current discount.
/// IsPriceInConflict is true when two discounts overlap and the engine sells at the store price until an administrator decides.
/// </summary>
public sealed record VariantPriceLookupResponse(
    Guid VariantId,
    Guid ProductId,
    string ProductCode,
    string Barcode,
    string ProductName,
    string? ProductNameUrdu,
    string SizeCode,
    string? ColorName,
    string? BrandName,
    decimal StockQuantity,
    long ListPrice,
    long StorePrice,
    long EffectiveSalePrice,
    long DiscountAmount,
    decimal? DiscountPercent,
    bool IsPriceInConflict,
    Guid? StoreId);
