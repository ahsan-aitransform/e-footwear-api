namespace EFootwear.Domain.Payroll;

/// <summary>How a salary is paid. The values mirror the CHECK on <c>hr.salary_payments.method</c>; <c>hr.pay_salary</c> credits the store cash for CASH and the bank for the others.</summary>
public static class SalaryPaymentMethodCodes
{
    public const string Cash = "CASH";
    public const string Bank = "BANK";
    public const string Other = "OTHER";

    public static readonly IReadOnlyList<string> All = [Cash, Bank, Other];

    public static bool IsValid(string? methodCode) => methodCode is Cash or Bank or Other;
}
