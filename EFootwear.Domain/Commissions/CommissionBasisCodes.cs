namespace EFootwear.Domain.Commissions;

/// <summary>
/// What the commission is worked out from (hr.commission_plans.basis): the net amount of the sale line, or the net amount minus its cost.
/// [BUSINESS DECISION REQUIRED] Which one a company uses is the company's choice per plan; no default is enforced here.
/// </summary>
public static class CommissionBasisCodes
{
    public const string NetSale = "NET_SALE";
    public const string GrossMargin = "GROSS_MARGIN";

    public static readonly IReadOnlyList<string> All = [NetSale, GrossMargin];

    public static bool IsValid(string? basisCode) => basisCode is NetSale or GrossMargin;
}
