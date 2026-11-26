namespace EFootwear.Domain.Commissions;

/// <summary>A percentage of the base, or a fixed amount for every piece sold (hr.commission_plans.calc_type and hr.commission_rules.calc_type).</summary>
public static class CommissionCalculationTypeCodes
{
    public const string Percent = "PERCENT";
    public const string FixedPerPiece = "FIXED_PER_PIECE";

    public static readonly IReadOnlyList<string> All = [Percent, FixedPerPiece];

    public static bool IsValid(string? calculationTypeCode) => calculationTypeCode is Percent or FixedPerPiece;
}
