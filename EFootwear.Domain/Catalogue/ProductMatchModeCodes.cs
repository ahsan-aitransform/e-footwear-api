namespace EFootwear.Domain.Catalogue;

/// <summary>How the Search Product screen matches the typed text (docx 4.3: Start, Contains, Ends; and the whole value).</summary>
public static class ProductMatchModeCodes
{
    public const string Start = "START";
    public const string Contains = "CONTAINS";
    public const string End = "END";
    public const string Exact = "EXACT";

    public static bool IsKnown(string? modeCode) => modeCode is Start or Contains or End or Exact;
}
