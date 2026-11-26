namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>A store a promotion is limited to.</summary>
public sealed record PromotionStoreResponse(Guid StoreId, string StoreName);
