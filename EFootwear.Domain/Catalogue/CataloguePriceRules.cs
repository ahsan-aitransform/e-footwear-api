using EFootwear.Domain.Common;

namespace EFootwear.Domain.Catalogue;

/// <summary>
/// Limits of prices and percentages in the catalogue. Money is whole rupees (decision D-009), so prices are <c>long</c>. The maximum is a
/// sanity limit that keeps every product of a bill inside a bigint total; it is not a business policy.
/// </summary>
public static class CataloguePriceRules
{
    public const long MaximumPrice = 999_999_999;
    public const decimal PercentageMaximum = 100m;

    /// <summary>core.pct has four decimals; the screens accept two.</summary>
    public const int PercentageDecimalPlaces = 2;

    public static void EnsureValidPrice(long price, string priceDescription)
    {
        if (price < 0)
            throw new DomainException(ErrorKind.Validation, $"The {priceDescription} cannot be negative.");
        if (price > MaximumPrice)
            throw new DomainException(ErrorKind.Validation, $"The {priceDescription} cannot be more than {MaximumPrice:N0}.");
    }

    /// <summary>A price can more than double (docx 4.8 has no limit); this is only a sanity limit against a mistyped number.</summary>
    public const decimal IncreasePercentageMaximum = 1000m;

    public static void EnsureValidIncreasePercentage(decimal percentage)
    {
        if (percentage < 0.01m || percentage > IncreasePercentageMaximum)
            throw new DomainException(ErrorKind.Validation, $"The increase must be between 0.01 and {IncreasePercentageMaximum:0}%.");
        if (decimal.Round(percentage, PercentageDecimalPlaces) != percentage)
            throw new DomainException(ErrorKind.Validation, $"The increase can have at most {PercentageDecimalPlaces} decimal places.");
    }

    /// <summary>A decrease of 100% or more would give a price of zero or less; the decrease must stay under 100.</summary>
    public static void EnsureValidDecreasePercentage(decimal percentage)
    {
        if (percentage < 0.01m || percentage >= PercentageMaximum)
            throw new DomainException(ErrorKind.Validation, "The decrease must be between 0.01 and 99.99%.");
        if (decimal.Round(percentage, PercentageDecimalPlaces) != percentage)
            throw new DomainException(ErrorKind.Validation, $"The decrease can have at most {PercentageDecimalPlaces} decimal places.");
    }

    public static void EnsureValidPercentage(decimal percentage, string percentageDescription, bool allowZero = false)
    {
        var lowest = allowZero ? 0m : 0.01m;
        if (percentage < lowest || percentage > PercentageMaximum)
            throw new DomainException(ErrorKind.Validation,
                $"The {percentageDescription} must be between {lowest.ToString(System.Globalization.CultureInfo.InvariantCulture)} and 100.");
        if (decimal.Round(percentage, PercentageDecimalPlaces) != percentage)
            throw new DomainException(ErrorKind.Validation, $"The {percentageDescription} can have at most {PercentageDecimalPlaces} decimal places.");
    }
}
