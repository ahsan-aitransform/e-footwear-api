namespace EFootwear.Domain.Core.Settings;

/// <summary>Print formats named in Part 11: thermal receipt, A4 and A5.</summary>
public static class PrintFormatCodes
{
    public const string Thermal = "THERMAL";
    public const string A4 = "A4";
    public const string A5 = "A5";

    public static readonly IReadOnlyList<string> All = [Thermal, A4, A5];
}
