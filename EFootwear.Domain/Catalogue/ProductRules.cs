using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;

namespace EFootwear.Domain.Catalogue;

/// <summary>
/// Validation of the product header (docx 4.1: short name, full name, Urdu name, classification, status, weight, description).
/// The code is not validated here: the database allocates it.
/// </summary>
public static class ProductRules
{
    public const int NameMaximumLength = 200;
    public const int DescriptionMaximumLength = 4000;
    public const int TaxCodeMaximumLength = 40;

    /// <summary>catalog.products.weight is numeric(10,3).</summary>
    public const decimal WeightMaximum = 9_999_999.999m;

    public const decimal TaxRateMaximum = 100m;

    public static string NormalizeName(string? name) => MasterListRules.NormalizeText(name);

    public static string? NormalizeOptionalText(string? text) => MasterListRules.NormalizeOptionalText(text);

    /// <summary>The description keeps its line breaks; only the ends are trimmed.</summary>
    public static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public static void EnsureValidHeader(string name, string? nameUrdu, string? description, decimal? weight,
                                         decimal taxRate, string? taxCode, string status)
    {
        if (name.Length == 0)
            throw new DomainException(ErrorKind.Validation, "The product name is required.");
        if (name.Length > NameMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"The product name can have at most {NameMaximumLength} characters.");
        if (nameUrdu is { Length: > NameMaximumLength })
            throw new DomainException(ErrorKind.Validation, $"The Urdu name can have at most {NameMaximumLength} characters.");
        if (description is { Length: > DescriptionMaximumLength })
            throw new DomainException(ErrorKind.Validation, $"The description can have at most {DescriptionMaximumLength} characters.");
        if (weight is < 0 or > WeightMaximum)
            throw new DomainException(ErrorKind.Validation, $"The weight must be between 0 and {WeightMaximum}.");
        if (taxRate is < 0 or > TaxRateMaximum)
            throw new DomainException(ErrorKind.Validation, "The tax rate must be between 0 and 100.");
        if (taxCode is { Length: > TaxCodeMaximumLength })
            throw new DomainException(ErrorKind.Validation, $"The tax code can have at most {TaxCodeMaximumLength} characters.");
        if (!ProductStatusCodes.IsKnown(status))
            throw new DomainException(ErrorKind.Validation, "The status must be Active or Inactive.");
    }
}
