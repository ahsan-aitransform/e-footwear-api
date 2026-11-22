namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>A size of a product as it is stored now, with its company list prices; the service compares it with the saved form to find what changed.</summary>
public sealed record ExistingVariantSnapshot(
    Guid VariantId,
    Guid? ColorId,
    Guid? SizeGroupId,
    string SizeCode,
    string Barcode,
    string Status,
    long PurchasePrice,
    long SalePrice,
    long RowVersion);
