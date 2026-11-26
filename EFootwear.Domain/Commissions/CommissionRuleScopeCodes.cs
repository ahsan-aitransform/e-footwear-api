namespace EFootwear.Domain.Commissions;

/// <summary>What a commission rule is about (hr.commission_rules.rule_scope). When several rules match a sold line the most specific wins: product, then category, then price band.</summary>
public static class CommissionRuleScopeCodes
{
    public const string Product = "PRODUCT";
    public const string Category = "CATEGORY";
    public const string PriceBand = "PRICE_BAND";

    public static readonly IReadOnlyList<string> All = [Product, Category, PriceBand];

    public static bool IsValid(string? scopeCode) => scopeCode is Product or Category or PriceBand;
}
