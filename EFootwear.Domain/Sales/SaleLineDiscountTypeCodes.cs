namespace EFootwear.Domain.Sales;

/// <summary>How the cashier entered a line discount: a percentage of the price, or rupees per unit (docx "Disc. % / Discount").</summary>
public static class SaleLineDiscountTypeCodes
{
    public const string None = "NONE";
    public const string Percent = "PERCENT";
    public const string Amount = "AMOUNT";

    public static bool IsValid(string? code) => code is None or Percent or Amount;
}
