namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>
/// Creates or replaces a special offer, a sale on stores or a sale on categories. A sale needs <see cref="DiscountPercent"/> and its stores (a sale on categories also its
/// categories); an offer ignores the percentage and may name no store, which means every store. <see cref="RowVersion"/> is required when changing one, so two people
/// editing the same promotion cannot overwrite each other.
/// </summary>
public sealed record SavePromotionRuleRequest(
    string Name,
    decimal? DiscountPercent,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive,
    IReadOnlyList<Guid>? StoreIds,
    IReadOnlyList<Guid>? CategoryIds,
    long? RowVersion);
