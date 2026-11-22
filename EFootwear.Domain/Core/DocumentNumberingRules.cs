using System.Text.RegularExpressions;
using EFootwear.Domain.Common;

namespace EFootwear.Domain.Core;

/// <summary>Rules for changing where a document number series continues.</summary>
public static partial class DocumentNumberingRules
{
    /// <summary>core.number_series.period_key of a series that never resets (the schema default).</summary>
    public static readonly DateOnly NeverResetsPeriodKey = new(1900, 1, 1);

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,59}$")]
    private static partial Regex SeriesCodePattern();

    public static string EnsureValidSeriesCode(string? seriesCode)
    {
        var code = (seriesCode ?? "").Trim();
        if (!SeriesCodePattern().IsMatch(code))
            throw new DomainException(ErrorKind.Validation, "Series code: 2 to 60 capital letters, digits or underscores.");
        return code;
    }

    /// <summary>
    /// Numbers must never repeat, so a series can only continue at its current value or a higher one. (This is a technical
    /// integrity rule, not a business policy: going back would hand out numbers that may already be on printed documents.)
    /// </summary>
    public static void EnsureNextValueDoesNotGoBack(long requestedNextValue, long? currentNextValue)
    {
        if (requestedNextValue < 1)
            throw new DomainException(ErrorKind.Validation, "The next number must be 1 or higher.");
        if (currentNextValue is { } current && requestedNextValue < current)
            throw new DomainException(ErrorKind.Validation,
                $"The next number cannot be lower than {current}: numbers that were already issued must not repeat.",
                ErrorCodes.DocumentNumberWouldRepeat);
    }
}
