namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>
/// One rule of a plan: a different rate or fixed amount for one product, one category or one price band, between two dates.
/// Exactly one of product, category and price band is filled, as the scope says. A rule is ended, never edited, so history stays true.
/// </summary>
public sealed record CommissionRuleResponse(
    Guid Id,
    string ScopeCode,
    Guid? ProductId,
    string? ProductName,
    Guid? CategoryId,
    string? CategoryName,
    Guid? PriceBandId,
    string? PriceBandName,
    string CalculationTypeCode,
    decimal? Rate,
    long? FixedAmountPerPiece,
    long MinimumLineAmount,
    int Priority,
    DateOnly ValidFrom,
    DateOnly? ValidTo);
