using EFootwear.Domain.Common;

namespace EFootwear.Domain.Sales;

/// <summary>
/// What the sales endpoints check before the database function is called, so a person gets a short, clear answer next to the field.
/// The limits mirror the checks inside sales.post_sale_invoice and sales.post_return_invoice, which stay the authority.
/// Not here, on purpose: discount stacking, maximum discount, return periods, refunds, tax: those are business decisions.
/// </summary>
public static class SaleRules
{
    public const int MaximumLinesPerInvoice = 200;
    public const decimal MaximumQuantityPerLine = 999_999m;
    public const int QuantityDecimalPlaces = 3;
    public const int DescriptionMaximumLength = 500;
    public const int ReasonMaximumLength = 200;
    public const int WalkInNameMaximumLength = 100;
    public const int WalkInMobileMaximumLength = 30;
    public const int SearchResultLimit = 20;
    public const int ReturnableLinesLookupLimit = 200;

    /// <summary>Quantity above zero with at most three decimals (the database domain core.qty is numeric(14,3)).</summary>
    public static void EnsureQuantityIsValid(decimal quantity, int lineNumber)
    {
        if (quantity <= 0 || quantity > MaximumQuantityPerLine || decimal.Round(quantity, QuantityDecimalPlaces) != quantity)
            throw new DomainException(ErrorKind.Validation,
                $"Line {lineNumber}: the quantity must be above zero, at most {MaximumQuantityPerLine:N0}, with at most {QuantityDecimalPlaces} decimals.");
    }

    /// <summary>Trims the text; empty becomes null; too long is refused with the name of the field.</summary>
    public static string? CleanOptionalText(string? text, int maximumLength, string fieldName)
    {
        var cleaned = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (cleaned is not null && cleaned.Length > maximumLength)
            throw new DomainException(ErrorKind.Validation, $"{fieldName} can have at most {maximumLength} characters.");
        return cleaned;
    }

    public static string CleanRequiredReason(string? reason, string fieldName)
    {
        var cleaned = CleanOptionalText(reason, ReasonMaximumLength, fieldName);
        return cleaned ?? throw new DomainException(ErrorKind.Validation, $"Give {fieldName.ToLowerInvariant()}.");
    }
}
