namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>A price history line to append. StoreId is the nil uuid for the company list price.</summary>
public sealed record PriceEventWriteData(
    Guid VariantId,
    Guid StoreId,
    string PriceType,
    long? OldPrice,
    long NewPrice,
    Guid? BatchId,
    string Reason);
