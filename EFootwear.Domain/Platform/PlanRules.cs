using System.Text.RegularExpressions;
using EFootwear.Domain.Common;

namespace EFootwear.Domain.Platform;

/// <summary>Validation of subscription plans (platform.plans). The limits themselves are chosen by the platform owner.</summary>
public static partial class PlanRules
{
    public const int NameMaximumLength = 80;
    public const int LimitMaximum = 1_000_000;

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,29}$")]
    private static partial Regex CodePattern();

    public static string EnsureValidCode(string? code)
    {
        var normalized = (code ?? "").Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(normalized))
            throw new DomainException(ErrorKind.Validation, "Plan code: 2 to 30 capital letters, digits or underscores, starting with a letter.");
        return normalized;
    }

    public static string EnsureValidName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > NameMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"Plan name is required (up to {NameMaximumLength} characters).");
        return trimmed;
    }

    public static void EnsureValidLimits(int maximumStores, int maximumUsers)
    {
        if (maximumStores < 1 || maximumStores > LimitMaximum)
            throw new DomainException(ErrorKind.Validation, $"Maximum stores must be between 1 and {LimitMaximum}.");
        if (maximumUsers < 1 || maximumUsers > LimitMaximum)
            throw new DomainException(ErrorKind.Validation, $"Maximum users must be between 1 and {LimitMaximum}.");
    }
}
