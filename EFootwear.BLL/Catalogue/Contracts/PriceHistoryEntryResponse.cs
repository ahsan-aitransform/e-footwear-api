namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>One line of the price history of a product (Part 9.2). StoreId is null for the company list price.</summary>
public sealed record PriceHistoryEntryResponse(
    Guid Id,
    Guid VariantId,
    string SizeCode,
    string Barcode,
    Guid? StoreId,
    string? StoreName,
    string PriceType,
    long? OldPrice,
    long NewPrice,
    Guid? BatchId,
    string? Reason,
    DateTimeOffset ChangedAt,
    string? ChangedByName);
