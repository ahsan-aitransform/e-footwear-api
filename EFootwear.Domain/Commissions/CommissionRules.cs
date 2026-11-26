using EFootwear.Domain.Common;

namespace EFootwear.Domain.Commissions;

/// <summary>
/// Validation of commission plans and rules (Part 16.2). The calculation itself is done by the database when a sale is posted
/// (hr.accrue_line_commission); these checks only keep a request inside what the tables accept and give a clear message first.
/// Money is whole rupees (decision D-009). A percentage is 0 to 100 with up to four decimals.
/// </summary>
public static class CommissionRules
{
    public const int PlanNameMaximumLength = 120;
    public const decimal MaximumPercentage = 100m;
    public const int PercentageDecimalPlaces = 4;
    public const int FirstSupportedYear = 2000;
    public const int LastSupportedYear = 2100;
    /// <summary>How many rows a commission report returns at most; the totals still cover every matching entry.</summary>
    public const int ReportRowLimit = 2000;

    public static void EnsureCalculationIsValid(string? calculationTypeCode, decimal? rate, long? fixedAmountPerPiece)
    {
        if (!CommissionCalculationTypeCodes.IsValid(calculationTypeCode))
            throw new DomainException(ErrorKind.Validation, "Choose how the commission is calculated: a percentage or a fixed amount per piece.");

        if (calculationTypeCode == CommissionCalculationTypeCodes.Percent)
        {
            if (rate is null) throw new DomainException(ErrorKind.Validation, "Enter the commission percentage.");
            if (rate < 0 || rate > MaximumPercentage)
                throw new DomainException(ErrorKind.Validation, "The commission percentage must be between 0 and 100.");
            if (decimal.Round(rate.Value, PercentageDecimalPlaces) != rate.Value)
                throw new DomainException(ErrorKind.Validation, $"The commission percentage can have at most {PercentageDecimalPlaces} decimals.");
            if (fixedAmountPerPiece is not null)
                throw new DomainException(ErrorKind.Validation, "A percentage commission has no fixed amount.");
        }
        else
        {
            if (fixedAmountPerPiece is null) throw new DomainException(ErrorKind.Validation, "Enter the fixed commission for each piece.");
            if (fixedAmountPerPiece < 0) throw new DomainException(ErrorKind.Validation, "The fixed commission cannot be negative.");
            if (rate is not null) throw new DomainException(ErrorKind.Validation, "A fixed commission has no percentage.");
        }
    }

    public static void EnsurePlanIsValid(string? name, string? appliesToCode, string? basisCode, long minimumStoreSales, DateOnly validFrom, DateOnly? validTo)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorKind.Validation, "Enter the name of the commission plan.");
        if (name.Trim().Length > PlanNameMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"The plan name can be at most {PlanNameMaximumLength} characters.");
        if (!CommissionPlanAppliesToCodes.IsValid(appliesToCode))
            throw new DomainException(ErrorKind.Validation, "Choose who the plan pays: salesmen or managers.");
        if (!CommissionBasisCodes.IsValid(basisCode))
            throw new DomainException(ErrorKind.Validation, "Choose what the commission is based on: net sale or gross margin.");
        if (minimumStoreSales < 0) throw new DomainException(ErrorKind.Validation, "The minimum store sales cannot be negative.");
        EnsureDateIsSupported(validFrom, "The first day of the plan");
        if (validTo is { } lastDay)
        {
            EnsureDateIsSupported(lastDay, "The last day of the plan");
            if (lastDay < validFrom) throw new DomainException(ErrorKind.Validation, "The last day of the plan cannot be before its first day.");
        }
    }

    public static void EnsureRuleIsValid(string? scopeCode, Guid? productId, Guid? categoryId, Guid? priceBandId, long minimumLineAmount, DateOnly validFrom, DateOnly? validTo)
    {
        if (!CommissionRuleScopeCodes.IsValid(scopeCode))
            throw new DomainException(ErrorKind.Validation, "Choose what the rule is about: a product, a category or a price band.");
        var matches = scopeCode switch
        {
            CommissionRuleScopeCodes.Product => productId is not null && categoryId is null && priceBandId is null,
            CommissionRuleScopeCodes.Category => categoryId is not null && productId is null && priceBandId is null,
            _ => priceBandId is not null && productId is null && categoryId is null,
        };
        if (!matches) throw new DomainException(ErrorKind.Validation, "Choose exactly the product, category or price band the rule is about.");
        if (minimumLineAmount < 0) throw new DomainException(ErrorKind.Validation, "The minimum line amount cannot be negative.");
        EnsureDateIsSupported(validFrom, "The first day of the rule");
        if (validTo is { } lastDay)
        {
            EnsureDateIsSupported(lastDay, "The last day of the rule");
            if (lastDay < validFrom) throw new DomainException(ErrorKind.Validation, "The last day of the rule cannot be before its first day.");
        }
    }

    public static void EnsureDateRangeIsValid(DateOnly fromDate, DateOnly toDate)
    {
        EnsureDateIsSupported(fromDate, "The first date");
        EnsureDateIsSupported(toDate, "The last date");
        if (fromDate > toDate) throw new DomainException(ErrorKind.Validation, "The first date must not be after the last date.");
    }

    private static void EnsureDateIsSupported(DateOnly date, string label)
    {
        if (date.Year < FirstSupportedYear || date.Year > LastSupportedYear)
            throw new DomainException(ErrorKind.Validation, $"{label} must be between the year {FirstSupportedYear} and {LastSupportedYear}.");
    }
}
