using EFootwear.Domain.Common;

namespace EFootwear.Domain.HumanResources;

/// <summary>Validation of the salary information of a staff member (Part 16.1). Money is whole rupees (decision D-009).</summary>
public static class StaffRules
{
    public const int StaffSearchTextMaximumLength = 100;

    /// <summary>A base salary is zero (not yet set, the value new staff start with) or more. A negative salary is refused by the database too.</summary>
    public static void EnsureSalaryTermsAreValid(long baseSalary, string? payFrequencyCode)
    {
        if (baseSalary < 0)
            throw new DomainException(ErrorKind.Validation, "The base salary cannot be negative.");
        if (!PayFrequencyCodes.IsValid(payFrequencyCode))
            throw new DomainException(ErrorKind.Validation, "Choose how often the person is paid (monthly, biweekly or weekly).");
    }
}
