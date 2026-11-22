namespace EFootwear.Domain.Accounting;

/// <summary>Which side of the books an opening balance is on (docx 2.18: the "Type" column, Debit or Credit).</summary>
public static class OpeningBalanceSideCodes
{
    public const string Debit = "DEBIT";
    public const string Credit = "CREDIT";

    public static bool IsValid(string? sideCode) => sideCode is Debit or Credit;
}
