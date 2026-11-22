namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>What the database knows about a size for its label; the service adds the number of copies.</summary>
public sealed record BarcodeLabelData(
    Guid VariantId,
    string Barcode,
    string ProductCode,
    string ProductName,
    string? ProductNameUrdu,
    string SizeCode,
    string? ColorName,
    string? BrandName,
    long SalePrice);
