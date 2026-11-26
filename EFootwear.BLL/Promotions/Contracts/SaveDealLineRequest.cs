namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>A product of a deal: the size, how many, and the price each is sold at inside the deal.</summary>
public sealed record SaveDealLineRequest(Guid VariantId, decimal Quantity, long NetUnitPrice);
