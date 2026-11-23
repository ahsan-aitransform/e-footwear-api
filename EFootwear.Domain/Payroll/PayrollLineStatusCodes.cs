namespace EFootwear.Domain.Payroll;

/// <summary>Status of one employee's line on a salary sheet (<c>hr.payroll_lines.status</c>). Salary can be paid only on an APPROVED line; a line turns PAID when nothing is outstanding.</summary>
public static class PayrollLineStatusCodes
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    public const string Paid = "PAID";
    public const string Cancelled = "CANCELLED";
}
