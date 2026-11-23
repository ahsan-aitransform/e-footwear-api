using EFootwear.Domain.Common;

namespace EFootwear.Domain.Payroll;

/// <summary>Validation shared by the salary sheet, the adjustments and the salary payments (docx 3.8, 3.9; Part 16.3). Money is whole rupees (decision D-009).</summary>
public static class PayrollRules
{
    public const int AdjustmentReasonMaximumLength = 300;
    public const int PaymentReferenceMaximumLength = 100;
    /// <summary>How many rows a payroll report returns at most; the totals still cover every matching line.</summary>
    public const int ReportRowLimit = 2000;

    public const int FirstSupportedYear = 2000;
    public const int LastSupportedYear = 2100;

    /// <summary>A salary month is always its first day (the database checks it); any day the person picked is moved there.</summary>
    public static DateOnly NormalizeToFirstDayOfMonth(DateOnly anyDayOfTheMonth)
    {
        if (anyDayOfTheMonth.Year < FirstSupportedYear || anyDayOfTheMonth.Year > LastSupportedYear)
            throw new DomainException(ErrorKind.Validation, $"Choose a salary month between {FirstSupportedYear} and {LastSupportedYear}.");
        return new DateOnly(anyDayOfTheMonth.Year, anyDayOfTheMonth.Month, 1);
    }

    public static void EnsureAdjustmentIsValid(string? adjustmentTypeCode, long amount, string? reason)
    {
        if (!PayrollAdjustmentTypeCodes.IsValid(adjustmentTypeCode))
            throw new DomainException(ErrorKind.Validation, "Choose the kind of adjustment: incentive, deduction or advance.");
        if (amount <= 0)
            throw new DomainException(ErrorKind.Validation, "The adjustment amount must be more than zero.");
        if (reason is { Length: > AdjustmentReasonMaximumLength })
            throw new DomainException(ErrorKind.Validation, $"The reason can be at most {AdjustmentReasonMaximumLength} characters.");
    }

    public static void EnsurePaymentIsValid(long amount, string? methodCode, string? reference)
    {
        if (amount <= 0)
            throw new DomainException(ErrorKind.Validation, "The payment amount must be more than zero.");
        if (!SalaryPaymentMethodCodes.IsValid(methodCode))
            throw new DomainException(ErrorKind.Validation, "Choose how the salary is paid: cash, bank or other.");
        if (reference is { Length: > PaymentReferenceMaximumLength })
            throw new DomainException(ErrorKind.Validation, $"The reference can be at most {PaymentReferenceMaximumLength} characters.");
    }
}
