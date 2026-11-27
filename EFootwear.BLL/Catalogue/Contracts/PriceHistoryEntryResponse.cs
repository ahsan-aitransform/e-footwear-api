namespace EFootwear.BLL.Catalogue.Contracts;

/// <summary>
/// One price change of a product (Part 9.2), whatever sizes it covered. StoreId is null for the company list price. A price type that did not
/// change has null values; Lowest and Highest differ only when sizes got different prices; Old is null when there was none or the sizes differed.
/// </summary>
public sealed record PriceHistoryEntryResponse(
    Guid? StoreId,
    string? StoreName,
    long? OldPurchasePrice,
    long? NewPurchasePriceLowest,
    long? NewPurchasePriceHighest,
    long? OldSalePrice,
    long? NewSalePriceLowest,
    long? NewSalePriceHighest,
    Guid? BatchId,
    string? Reason,
    DateTimeOffset ChangedAt,
    string? ChangedByName);
