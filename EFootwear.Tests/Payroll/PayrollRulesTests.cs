using EFootwear.Domain.Common;
using EFootwear.Domain.HumanResources;
using EFootwear.Domain.Payroll;

namespace EFootwear.Tests.Payroll;

/// <summary>The validation rules of staff salary terms, payroll adjustments and salary payments (whole rupees, no database needed).</summary>
public class PayrollRulesTests
{
    [Theory]
    [InlineData("2026-10-01", "2026-10-01")]
    [InlineData("2026-10-17", "2026-10-01")]
    [InlineData("2026-12-31", "2026-12-01")]
    public void Any_day_of_a_month_is_moved_to_its_first_day(string day, string expectedFirstDay) =>
        Assert.Equal(DateOnly.Parse(expectedFirstDay), PayrollRules.NormalizeToFirstDayOfMonth(DateOnly.Parse(day)));

    [Theory]
    [InlineData("1999-12-31")]
    [InlineData("2101-01-01")]
    public void A_month_outside_the_supported_years_is_refused(string day) =>
        Assert.Equal(ErrorKind.Validation, Assert.Throws<DomainException>(() => PayrollRules.NormalizeToFirstDayOfMonth(DateOnly.Parse(day))).Kind);

    [Theory]
    [InlineData("INCENTIVE", 1)]
    [InlineData("DEDUCTION", 5_000)]
    [InlineData("ADVANCE", 25_000)]
    public void A_valid_adjustment_is_accepted(string type, long amount) => PayrollRules.EnsureAdjustmentIsValid(type, amount, "Because");

    [Theory]
    [InlineData("INCENTIVE", 0)]
    [InlineData("INCENTIVE", -1)]
    [InlineData("BONUS", 100)]
    [InlineData(null, 100)]
    public void A_bad_adjustment_is_refused(string? type, long amount) =>
        Assert.Equal(ErrorKind.Validation, Assert.Throws<DomainException>(() => PayrollRules.EnsureAdjustmentIsValid(type, amount, null)).Kind);

    [Fact]
    public void A_too_long_adjustment_reason_is_refused() =>
        Assert.Throws<DomainException>(() => PayrollRules.EnsureAdjustmentIsValid("INCENTIVE", 100, new string('x', PayrollRules.AdjustmentReasonMaximumLength + 1)));

    [Theory]
    [InlineData(1, "CASH")]
    [InlineData(20_000, "BANK")]
    [InlineData(999_999_999_999, "OTHER")]
    public void A_valid_salary_payment_is_accepted(long amount, string method) => PayrollRules.EnsurePaymentIsValid(amount, method, "Ref 1");

    [Theory]
    [InlineData(0, "CASH")]
    [InlineData(-100, "CASH")]
    [InlineData(100, "CHEQUE")]
    [InlineData(100, null)]
    public void A_bad_salary_payment_is_refused(long amount, string? method) =>
        Assert.Equal(ErrorKind.Validation, Assert.Throws<DomainException>(() => PayrollRules.EnsurePaymentIsValid(amount, method, null)).Kind);

    [Theory]
    [InlineData(0, "MONTHLY")]
    [InlineData(35_000, "BIWEEKLY")]
    [InlineData(8_000, "WEEKLY")]
    public void Valid_salary_terms_are_accepted(long baseSalary, string frequency) => StaffRules.EnsureSalaryTermsAreValid(baseSalary, frequency);

    [Theory]
    [InlineData(-1, "MONTHLY")]
    [InlineData(1_000, "DAILY")]
    [InlineData(1_000, null)]
    public void Bad_salary_terms_are_refused(long baseSalary, string? frequency) =>
        Assert.Equal(ErrorKind.Validation, Assert.Throws<DomainException>(() => StaffRules.EnsureSalaryTermsAreValid(baseSalary, frequency)).Kind);
}
