using System.Text.RegularExpressions;
using EFootwear.Domain.Common;

namespace EFootwear.Domain.Core;

/// <summary>The validation rules for a store's code and name.</summary>
public static partial class StoreRules
{
    public const int CodeMaximumLength = 20;
    public const int NameMaximumLength = 120;

    [GeneratedRegex("^[A-Za-z0-9-]{1,20}$")]
    private static partial Regex CodePattern();

    public static string EnsureValidName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > NameMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"Store name is required (up to {NameMaximumLength} characters).");
        return trimmed;
    }

    public static string? NormalizeAddress(string? address) =>
        Settings.SettingsValueNormalizer.NormalizeOptionalText(address, Settings.SettingsLimits.AddressMaximumLength, "Store address");

    public static string? NormalizePhone(string? phone) =>
        Settings.SettingsValueNormalizer.NormalizeOptionalText(phone, Settings.SettingsLimits.PhoneNumberMaximumLength, "Store phone");

    public static void EnsureValid(string code, string name)
    {
        if (!CodePattern().IsMatch((code ?? "").Trim()))
            throw new DomainException(ErrorKind.Validation,
                $"Store code: 1 to {CodeMaximumLength} letters, digits or dashes.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaximumLength)
            throw new DomainException(ErrorKind.Validation,
                $"Store name is required (up to {NameMaximumLength} characters).");
    }
}
