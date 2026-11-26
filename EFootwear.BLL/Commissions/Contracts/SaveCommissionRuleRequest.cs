namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>Adds a rule to a plan. Give the product, the category or the price band that <see cref="ScopeCode"/> names, and nothing else.</summary>
public sealed record SaveCommissionRuleRequest(
    string ScopeCode,
    Guid? ProductId,
    Guid? CategoryId,
    Guid? PriceBandId,
    string CalculationTypeCode,
    decimal? Rate,
    long? FixedAmountPerPiece,
    long MinimumLineAmount,
    int Priority,
    DateOnly ValidFrom,
    DateOnly? ValidTo);
