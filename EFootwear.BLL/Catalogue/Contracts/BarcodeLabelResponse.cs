namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>What one kind of label shows. The screen repeats it Copies times and draws the barcode (Code 128) itself.</summary>
public sealed record BarcodeLabelResponse(
    Guid VariantId,
    string Barcode,
    string ProductCode,
    string ProductName,
    string? ProductNameUrdu,
    string SizeCode,
    string? ColorName,
    string? BrandName,
    long SalePrice,
    int Copies);
