using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;

namespace EFootwear.Domain.Promotions;

/// <summary>
/// Validation of special offers, sales and deals (docx 2.12 to 2.16, Part 18). The prices themselves come from the database engine (promo.rebuild_effective_prices);
/// these checks keep a request inside what the tables accept and give a clear message first. Money is whole rupees (decision D-009); a discount percentage is above 0 and up to 100
/// with at most two decimals (promo.rules.discount_value is numeric(12,2)). No stacking rule is decided here: when two promotions cover the same size the engine sells it
/// at the normal price until an administrator chooses one (Promotion Conflicts).
/// </summary>
public static class PromotionRules
{
    public const int NameMaximumLength = 120;
    public const decimal MaximumPercentage = 100m;
    public const int PercentageDecimalPlaces = 2;
    public const int FirstSupportedYear = 2000;
    public const int LastSupportedYear = 2100;
    public const int MaximumDealLines = 50;
    public const int MaximumOfferLinesPerRequest = 5000;

    public static string NormalizeName(string? name) => (name ?? "").Trim();

    public static void EnsureNameIsValid(string name, string what)
    {
        if (name.Length == 0) throw new DomainException(ErrorKind.Validation, $"Enter the name of the {what}.");
        if (name.Length > NameMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"The name of the {what} can be at most {NameMaximumLength} characters.");
    }

    public static void EnsurePercentageIsValid(decimal? percentage)
    {
        if (percentage is null) throw new DomainException(ErrorKind.Validation, "Enter the discount percentage.");
        if (percentage <= 0 || percentage > MaximumPercentage)
            throw new DomainException(ErrorKind.Validation, "The discount percentage must be more than 0 and at most 100.");
        if (decimal.Round(percentage.Value, PercentageDecimalPlaces) != percentage.Value)
            throw new DomainException(ErrorKind.Validation, $"The discount percentage can have at most {PercentageDecimalPlaces} decimals.");
    }

    public static void EnsureWindowIsValid(DateTimeOffset startsAt, DateTimeOffset? endsAt)
    {
        EnsureYearIsSupported(startsAt, "The start");
        if (endsAt is not { } end) return;
        EnsureYearIsSupported(end, "The end");
        if (end <= startsAt) throw new DomainException(ErrorKind.Validation, "The end must be after the start.");
    }

    public static void EnsureDealIsValid(string name, string barcode, IReadOnlyList<(Guid VariantId, decimal Quantity, long NetUnitPrice)> lines)
    {
        EnsureNameIsValid(name, "deal");
        if (barcode.Length > 0) VariantRules.EnsureValidBarcode(barcode);
        if (lines.Count == 0) throw new DomainException(ErrorKind.Validation, "A deal needs at least one product.");
        if (lines.Count > MaximumDealLines)
            throw new DomainException(ErrorKind.Validation, $"A deal can have at most {MaximumDealLines} products.");
        if (lines.Select(line => line.VariantId).Distinct().Count() != lines.Count)
            throw new DomainException(ErrorKind.Validation, "The same size is in the deal twice. Raise its quantity instead.");
        foreach (var line in lines)
        {
            if (line.Quantity <= 0 || line.Quantity != decimal.Truncate(line.Quantity))
                throw new DomainException(ErrorKind.Validation, "The quantity of every product in a deal must be a whole number of at least 1.");
            if (line.NetUnitPrice < 0) throw new DomainException(ErrorKind.Validation, "The net price of a product cannot be negative.");
        }
    }

    private static void EnsureYearIsSupported(DateTimeOffset moment, string label)
    {
        if (moment.Year < FirstSupportedYear || moment.Year > LastSupportedYear)
            throw new DomainException(ErrorKind.Validation, $"{label} must be between the year {FirstSupportedYear} and {LastSupportedYear}.");
    }
}
