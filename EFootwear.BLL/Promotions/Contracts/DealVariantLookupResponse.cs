namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>The size found by a scanned barcode when building a deal, with its current sale price.</summary>
public sealed record DealVariantLookupResponse(Guid VariantId, string Barcode, string ProductCode, string ProductName, string SizeCode, long SalePrice);
