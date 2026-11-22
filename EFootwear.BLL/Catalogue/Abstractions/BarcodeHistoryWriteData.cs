namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>A barcode history line to append.</summary>
public sealed record BarcodeHistoryWriteData(Guid VariantId, string OldBarcode, string NewBarcode, Guid? BatchId, string? Reason);
