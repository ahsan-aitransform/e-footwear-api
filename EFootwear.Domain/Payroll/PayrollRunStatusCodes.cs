namespace EFootwear.Domain.Payroll;

/// <summary>Status of a monthly salary sheet of one store (<c>hr.payroll_runs.status</c>). Only a DRAFT can be rebuilt or approved; an approved sheet has posted its salary expense.</summary>
public static class PayrollRunStatusCodes
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    public const string Cancelled = "CANCELLED";
}
