namespace EFootwear.Domain.Core.Settings;

/// <summary>
/// Printing: default print format (Part 11), print language and right-to-left printing (Part 7), logo on documents,
/// barcode label size and receipt copies. Company default; a store may override any field. Null = not configured / inherit.
/// </summary>
public sealed record PrintingSettings(
    string? DefaultPrintFormat,
    string? PrintLanguageCode,
    bool? PrintRightToLeft,
    bool? PrintLogoOnDocuments,
    int? BarcodeLabelWidthMillimetres,
    int? BarcodeLabelHeightMillimetres,
    int? ReceiptCopies);
