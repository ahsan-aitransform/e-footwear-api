namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>Which size currently carries a barcode (used to check a replacement file).</summary>
public sealed record VariantBarcodeOwner(Guid VariantId, string Barcode, string ProductName, string SizeCode);
