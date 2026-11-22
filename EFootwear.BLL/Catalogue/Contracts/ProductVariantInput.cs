namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// One size row of the New Product screen. VariantId is empty for a new size. A blank barcode is replaced by the system's proposal
/// (product code + S + size). RowVersion of an existing size is the one the client loaded. Status is Active unless the size is switched off.
/// </summary>
public sealed record ProductVariantInput(
    Guid? VariantId,
    Guid? ColorId,
    Guid? SizeGroupId,
    string SizeCode,
    string? Barcode,
    long PurchasePrice,
    long SalePrice,
    long? RowVersion = null,
    string? Status = null);
