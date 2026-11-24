using EFootwear.Domain.Common;

namespace EFootwear.Domain.Inventory;

/// <summary>
/// What the inventory endpoints check before a database function is called, so a person gets a short, clear answer next to the field.
/// The limits mirror the checks inside the functions of migration 0400, which stay the authority.
/// Not here, on purpose: who may adjust, audit or approve and up to which amount, backdating, negative stock, the accounting treatment, how long a transfer may be
/// in transit, who carries a loss, the effect of a claim on a vendor account, reconciliation tolerances: those are business decisions.
/// </summary>
public static class InventoryRules
{
    public const int MaximumLinesPerAdjustment = 500;
    public const int MaximumLinesPerCountUpdate = 2000;
    public const int MaximumPiecesPerScan = 200;
    public const int MaximumPiecesPerAuditScan = 1000;
    public const int MaximumDestinationStoresPerMultiTransfer = 20;
    public const int MaximumLinesPerMultiTransferDestination = 200;
    public const decimal MaximumQuantityPerLine = 999_999m;
    public const int QuantityDecimalPlaces = 3;
    public const int DescriptionMaximumLength = 500;
    public const int ReasonMaximumLength = 200;
    public const int SearchResultLimit = 20;
    public const int MaximumFileRows = 5000;

    /// <summary>A non-zero quantity with at most three decimals (the database domain core.qty is numeric(14,3)); the sign is the direction.</summary>
    public static void EnsureSignedQuantityIsValid(decimal quantity, int lineNumber)
    {
        if (quantity == 0 || Math.Abs(quantity) > MaximumQuantityPerLine || decimal.Round(quantity, QuantityDecimalPlaces) != quantity)
            throw new DomainException(ErrorKind.Validation,
                $"Line {lineNumber}: the quantity must not be zero, at most {MaximumQuantityPerLine:N0}, with at most {QuantityDecimalPlaces} decimals.");
    }

    /// <summary>A quantity above zero with at most three decimals.</summary>
    public static void EnsureQuantityIsValid(decimal quantity, string fieldName)
    {
        if (quantity <= 0 || quantity > MaximumQuantityPerLine || decimal.Round(quantity, QuantityDecimalPlaces) != quantity)
            throw new DomainException(ErrorKind.Validation,
                $"{fieldName} must be above zero, at most {MaximumQuantityPerLine:N0}, with at most {QuantityDecimalPlaces} decimals.");
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

    public static string CleanBarcode(string? barcode)
    {
        var cleaned = barcode?.Trim();
        if (string.IsNullOrEmpty(cleaned))
            throw new DomainException(ErrorKind.Validation, "Scan or type a barcode.");
        if (cleaned.Length > 48)
            throw new DomainException(ErrorKind.Validation, "A barcode can have at most 48 characters.");
        return cleaned;
    }
}
