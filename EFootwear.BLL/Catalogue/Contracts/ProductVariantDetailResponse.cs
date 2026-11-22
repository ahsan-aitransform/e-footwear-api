namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>One size of a product with its company list prices, as the New Product screen edits them. The row version guards against a lost update.</summary>
public sealed record ProductVariantDetailResponse(
    Guid VariantId,
    Guid? ColorId,
    Guid? SizeGroupId,
    string SizeCode,
    string Barcode,
    string Status,
    long PurchasePrice,
    long SalePrice,
    long RowVersion);
