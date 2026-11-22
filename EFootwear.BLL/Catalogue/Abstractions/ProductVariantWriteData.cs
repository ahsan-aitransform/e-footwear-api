namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>A checked size row: the barcode is final (typed or built from the product code) and the status is set.</summary>
public sealed record ProductVariantWriteData(
    Guid? VariantId,
    Guid? ColorId,
    Guid? SizeGroupId,
    string SizeCode,
    string Barcode,
    string Status,
    long PurchasePrice,
    long SalePrice,
    long? RowVersion);
