namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>One promotion that covers a size in conflict, with the price it would give.</summary>
public sealed record PromotionConflictCandidateResponse(Guid RuleId, string RuleName, string KindCode, long SalePrice, decimal? DiscountPercent);
