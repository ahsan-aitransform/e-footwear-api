using System.Text.RegularExpressions;
using EFootwear.Domain.Common;

namespace EFootwear.Domain.Accounting;

/// <summary>Rules of the chart of accounts (docx 2.1 to 2.3): code shapes, title limits and the next group code.</summary>
public static partial class ChartOfAccountsRules
{
    public const int TitleMaximumLength = 120;
    public const int GroupCodeLength = 3;
    public const int HighestGroupSerial = 99;

    [GeneratedRegex("^[1-6][0-9]{2}$")]
    private static partial Regex GroupCodePattern();

    public static bool IsValidGroupCode(string? groupCode) => groupCode is not null && GroupCodePattern().IsMatch(groupCode);

    /// <summary>Normalises a title and checks it (required, not too long).</summary>
    public static string NormalizeTitle(string? title, string what)
    {
        var normalized = string.Join(' ', (title ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
            throw new DomainException(ErrorKind.Validation, $"{what}: the title is required.");
        if (normalized.Length > TitleMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"{what}: the title can have at most {TitleMaximumLength} characters.");
        return normalized;
    }

    /// <summary>
    /// The next free group code of a main type: the digit of the type followed by the next two-digit serial (docx 2.2: 107 is
    /// followed by 108). The first group of a type is x01.
    /// </summary>
    public static string NextGroupCode(AccountMainType mainType, IEnumerable<string> existingGroupCodes)
    {
        var highestSerial = existingGroupCodes
            .Where(code => IsValidGroupCode(code) && code[0] == mainType.DigitPrefix)
            .Select(code => int.Parse(code.AsSpan(1)))
            .DefaultIfEmpty(0)
            .Max();
        if (highestSerial >= HighestGroupSerial)
            throw new DomainException(ErrorKind.Conflict, $"{mainType.DisplayName} already has the highest possible group number ({mainType.DigitPrefix}{HighestGroupSerial}).");
        return $"{mainType.DigitPrefix}{highestSerial + 1:00}";
    }
}
