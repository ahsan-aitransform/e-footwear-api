namespace EFootwear.BLL.Promotions.Contracts;

/// <summary>Chooses the promotion that wins for the size. The choice is kept while the set of promotions covering the size stays the same.</summary>
public sealed record ResolvePromotionConflictRequest(Guid ChosenRuleId);
