namespace EFootwear.Domain.Commissions;

/// <summary>Who a commission plan pays: the salesman named on a sale line, or the manager of the store the sale was made in (hr.commission_plans.applies_to).</summary>
public static class CommissionPlanAppliesToCodes
{
    public const string Salesman = "SALESMAN";
    public const string Manager = "MANAGER";

    public static readonly IReadOnlyList<string> All = [Salesman, Manager];

    public static bool IsValid(string? appliesToCode) => appliesToCode is Salesman or Manager;
}
