namespace EFootwear.Domain.Platform;

/// <summary>platform.companies.rounding_mode: how a computed customer-facing amount becomes whole rupees (core.round_money).</summary>
public static class RoundingModeCodes
{
    public const string Floor = "FLOOR";
    public const string Nearest = "NEAREST";

    public static readonly IReadOnlyList<string> All = [Floor, Nearest];
}
