namespace EFootwear.Domain.Core.Settings;

/// <summary>The time display formats: 12 hour with AM/PM or 24 hour.</summary>
public static class TimeFormatCodes
{
    public const string TwelveHour = "12H";
    public const string TwentyFourHour = "24H";

    public static readonly IReadOnlyList<string> All = [TwelveHour, TwentyFourHour];
}
