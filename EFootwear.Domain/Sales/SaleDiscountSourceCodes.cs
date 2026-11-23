namespace EFootwear.Domain.Sales;

/// <summary>Where the discount of a stored sale line came from (sales.invoice_lines.discount_source).</summary>
public static class SaleDiscountSourceCodes
{
    /// <summary>Sold at the shelf price.</summary>
    public const string None = "NONE";

    /// <summary>A promotion rule of the pricing engine applied.</summary>
    public const string Rule = "RULE";

    /// <summary>The cashier changed the price or gave a discount.</summary>
    public const string Manual = "MANUAL";

    public const string Bundle = "BUNDLE";
}
