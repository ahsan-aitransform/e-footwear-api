namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>
/// A special offer, a sale on stores or a sale on categories (all are promo.rules, told apart by <see cref="KindCode"/>). <see cref="DiscountPercent"/> is the discount of a
/// sale; an offer has none of its own, each product of the offer carries its percentage. <see cref="PhaseCode"/> says where it stands today (see PromotionPhaseCodes).
/// An empty store list means every store (offers only). The list endpoints fill the counts and leave the lists empty.
/// </summary>
public sealed record PromotionRuleResponse(
    Guid Id,
    string KindCode,
    string Name,
    decimal? DiscountPercent,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive,
    string PhaseCode,
    int StoreCount,
    int CategoryCount,
    int LineCount,
    IReadOnlyList<PromotionStoreResponse> Stores,
    IReadOnlyList<PromotionCategoryResponse> Categories,
    long RowVersion);
