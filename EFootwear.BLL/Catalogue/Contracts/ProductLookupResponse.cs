namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// One size of an active product as the product pickers of other screens need it (ledger, discounts on stores, barcode labels):
/// names and codes only, never prices or stock, so a screen that may not see prices can still pick a product.
/// </summary>
public sealed record ProductLookupResponse(
    Guid ProductId,
    Guid VariantId,
    string ProductCode,
    string ProductName,
    string Barcode,
    string SizeCode,
    string? ColorName,
    string? BrandName);
