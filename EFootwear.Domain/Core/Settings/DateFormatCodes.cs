namespace EFootwear.Domain.Core.Settings;

/// <summary>The date display formats a company can choose. The business date logic itself is not affected, only the display.</summary>
public static class DateFormatCodes
{
    public const string DayMonthYear = "DD/MM/YYYY";
    public const string MonthDayYear = "MM/DD/YYYY";
    public const string YearMonthDay = "YYYY-MM-DD";

    public static readonly IReadOnlyList<string> All = [DayMonthYear, MonthDayYear, YearMonthDay];
}
