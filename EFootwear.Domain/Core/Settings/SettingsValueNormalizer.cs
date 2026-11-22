using System.Text.RegularExpressions;
using EFootwear.Domain.Common;

namespace EFootwear.Domain.Core.Settings;

/// <summary>Small helpers shared by the settings validation rules: trimming, length limits, allowed lists.</summary>
public static partial class SettingsValueNormalizer
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    /// <summary>Trims the text; an empty text becomes null (meaning "not set"). Refuses a text longer than the limit.</summary>
    public static string? NormalizeOptionalText(string? value, int maximumLength, string fieldDescription)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maximumLength)
            throw new DomainException(ErrorKind.Validation, $"{fieldDescription} can have at most {maximumLength} characters.");
        return trimmed;
    }

    public static string? NormalizeOptionalEmail(string? value, string fieldDescription)
    {
        var email = NormalizeOptionalText(value, SettingsLimits.EmailMaximumLength, fieldDescription);
        if (email is not null && !EmailPattern().IsMatch(email))
            throw new DomainException(ErrorKind.Validation, $"{fieldDescription} is not a valid email address.");
        return email;
    }

    /// <summary>The value must be null (not configured) or one of the allowed codes.</summary>
    public static string? EnsureOptionalCode(string? value, IReadOnlyList<string> allowedCodes, string fieldDescription)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var code = value.Trim();
        if (!allowedCodes.Contains(code))
            throw new DomainException(ErrorKind.Validation,
                $"{fieldDescription} must be one of: {string.Join(", ", allowedCodes)}.");
        return code;
    }

    /// <summary>A percentage of core.pct: 0 to 100 with at most four decimal places (numeric(7,4)).</summary>
    public static decimal? EnsureOptionalPercentage(decimal? value, string fieldDescription)
    {
        if (value is null) return null;
        if (value < 0 || value > 100)
            throw new DomainException(ErrorKind.Validation, $"{fieldDescription} must be between 0 and 100.");
        if (decimal.Round(value.Value, SettingsLimits.PercentageDecimalPlaces) != value)
            throw new DomainException(ErrorKind.Validation,
                $"{fieldDescription} can have at most {SettingsLimits.PercentageDecimalPlaces} decimal places.");
        return value;
    }

    public static int? EnsureOptionalIntegerInRange(int? value, int minimum, int maximum, string fieldDescription)
    {
        if (value is null) return null;
        if (value < minimum || value > maximum)
            throw new DomainException(ErrorKind.Validation, $"{fieldDescription} must be between {minimum} and {maximum}.");
        return value;
    }
}
