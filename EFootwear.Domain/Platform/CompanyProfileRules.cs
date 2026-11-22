using System.Text.RegularExpressions;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core.Settings;

namespace EFootwear.Domain.Platform;

/// <summary>Validation of the legal and regional company profile (the fields of platform.companies other than name and invoice text).</summary>
public static partial class CompanyProfileRules
{
    public const int LegalNameMaximumLength = 200;
    public const int TaxNumberMaximumLength = 40;
    public const int TimezoneMaximumLength = 64;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCodePattern();

    public static string? NormalizeLegalName(string? value) =>
        SettingsValueNormalizer.NormalizeOptionalText(value, LegalNameMaximumLength, "Legal name");

    public static string? NormalizeTaxNumber(string? value, string fieldDescription) =>
        SettingsValueNormalizer.NormalizeOptionalText(value, TaxNumberMaximumLength, fieldDescription);

    public static string NormalizeTimezone(string? value)
    {
        var timezone = (value ?? "").Trim();
        if (timezone.Length == 0 || timezone.Length > TimezoneMaximumLength)
            throw new DomainException(ErrorKind.Validation, "Time zone is required (for example Asia/Karachi).");
        return timezone;   // the database checks it against pg_timezone_names
    }

    public static string NormalizeCurrencyCode(string? value)
    {
        var code = (value ?? "").Trim().ToUpperInvariant();
        if (!CurrencyCodePattern().IsMatch(code))
            throw new DomainException(ErrorKind.Validation, "Currency code must be three letters, for example PKR.");
        return code;
    }

    public static string NormalizeRoundingMode(string? value) =>
        SettingsValueNormalizer.EnsureOptionalCode(value, RoundingModeCodes.All, "Rounding mode")
        ?? throw new DomainException(ErrorKind.Validation, "Rounding mode is required.");

    public static string NormalizePriceBandBasis(string? value) =>
        SettingsValueNormalizer.EnsureOptionalCode(value, PriceBandBasisCodes.All, "Price band basis")
        ?? throw new DomainException(ErrorKind.Validation, "Price band basis is required.");
}
