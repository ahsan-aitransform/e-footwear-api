namespace EFootwear.Domain.Payroll;

/// <summary>
/// The three kinds of payroll adjustment of <c>hr.payroll_adjustments</c>. An incentive adds to the net pay; a deduction and an advance
/// are both taken off it (the docx "Deduction" column: advances, absences).
/// </summary>
public static class PayrollAdjustmentTypeCodes
{
    public const string Incentive = "INCENTIVE";
    public const string Deduction = "DEDUCTION";
    public const string Advance = "ADVANCE";

    public static readonly IReadOnlyList<string> All = [Incentive, Deduction, Advance];

    public static bool IsValid(string? adjustmentTypeCode) => adjustmentTypeCode is Incentive or Deduction or Advance;
}
