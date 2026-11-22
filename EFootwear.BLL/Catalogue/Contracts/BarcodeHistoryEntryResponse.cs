namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>One line of the barcode history of a size (Part 9.1: complete history of barcode replacement).</summary>
public sealed record BarcodeHistoryEntryResponse(
    Guid Id,
    Guid VariantId,
    string ProductName,
    string SizeCode,
    string OldBarcode,
    string NewBarcode,
    Guid? BatchId,
    string? Reason,
    DateTimeOffset ChangedAt,
    string? ChangedByName);
