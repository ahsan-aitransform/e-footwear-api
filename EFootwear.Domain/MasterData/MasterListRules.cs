using EFootwear.Domain.Common;

namespace EFootwear.Domain.MasterData;

/// <summary>
/// Validation rules shared by every master list (docx 2.4 and the common master-list pattern: the name is required, trimmed and
/// unique without regard to case; the database also enforces the uniqueness).
/// </summary>
public static class MasterListRules
{
    public const int NameMaximumLength = 120;
    public const int CodeMaximumLength = 40;
    public const int SortOrderMaximum = 100_000;

    /// <summary>Trims and collapses inner runs of white space so "  Men   Shoes " and "Men Shoes" are the same name.</summary>
    public static string NormalizeText(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string? NormalizeOptionalText(string? text)
    {
        var normalized = NormalizeText(text);
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>Throws a validation error when the values are not acceptable. Returns nothing: callers normalise the text themselves.</summary>
    public static void EnsureValid(string name, string? nameUrdu, string? code, int sortOrder, string displayName)
    {
        if (name.Length == 0)
            throw new DomainException(ErrorKind.Validation, $"{displayName}: the name is required.");
        if (name.Length > NameMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"{displayName}: the name can have at most {NameMaximumLength} characters.");
        if (nameUrdu is { Length: > NameMaximumLength })
            throw new DomainException(ErrorKind.Validation, $"{displayName}: the Urdu name can have at most {NameMaximumLength} characters.");
        if (code is { Length: > CodeMaximumLength })
            throw new DomainException(ErrorKind.Validation, $"{displayName}: the code can have at most {CodeMaximumLength} characters.");
        if (sortOrder is < 0 or > SortOrderMaximum)
            throw new DomainException(ErrorKind.Validation, $"{displayName}: the sort order must be between 0 and {SortOrderMaximum}.");
    }
}
