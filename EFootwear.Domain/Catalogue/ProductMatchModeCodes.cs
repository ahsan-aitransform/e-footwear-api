namespace EFootwear.Domain.Catalogue;

/// <summary>How the Search Product screen matches the typed text (docx 4.3: Start, Contains, Ends).</summary>
public static class ProductMatchModeCodes
{
    public const string Start = "START";
    public const string Contains = "CONTAINS";
    public const string End = "END";

    public static bool IsKnown(string? modeCode) => modeCode is Start or Contains or End;
}
