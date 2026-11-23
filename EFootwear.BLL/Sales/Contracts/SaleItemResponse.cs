namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// An item found by barcode or name for the sale screens. <paramref name="SuggestedUnitPrice"/> is the price the item sells for now (the store price or the
/// list price, lowered by a promotion that applies). A price different from it, or any discount, needs permission when the sale is saved.
/// </summary>
public sealed record SaleItemResponse(
    Guid VariantId,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string? ProductNameUrdu,
    string? UnitName,
    string SizeCode,
    string? ColorName,
    string Barcode,
    decimal StockQuantity,
    long ListPrice,
    long SuggestedUnitPrice,
    long PromotionDiscountPerUnit,
    bool HasPromotionConflict,
    decimal TaxRatePercentage,
    bool HasPicture);
