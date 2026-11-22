namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>A checked row of a barcode replacement file: what it would change, or why it cannot be applied (Problem).</summary>
public sealed record BarcodeReplacementRowResponse(
    int RowNumber,
    string OldBarcode,
    string NewBarcode,
    string? ProductName,
    string? SizeCode,
    bool IsValid,
    string? Problem);
