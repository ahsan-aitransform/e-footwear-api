using EFootwear.Domain.Security;

namespace EFootwear.Domain.Catalogue;

/// <summary>
/// The figures the product grids show next to the prices (docx 4.2 and 4.10). Only percentages are decimals; money stays whole rupees.
///   Net price = sale price minus discount.
///   Margin % = (net price - purchase price) / net price x 100   (docx 4.2: profit as a share of what the customer pays)
///   Markup % = (net price - purchase price) / purchase price x 100 (docx 4.10 calls it Profit %: profit as a share of the cost)
/// The two are different figures for the same product (docx findings: 44.94 against 81.64), so the screens label each one.
/// A negative value means the product is sold below its cost. When the divisor is zero the figure is undefined and null is returned.
/// </summary>
public static class ProductPricingFormulas
{
    /// <summary>Margin and markup are for the company owner only; a role rule, so that no permission grant can open them to anyone else.</summary>
    public static bool CanViewMargin(string roleCode) => roleCode == RoleCodes.CompanyOwner;

    public static long NetPrice(long salePrice, long discountAmount) => Math.Max(salePrice - discountAmount, 0);

    public static decimal? MarginPercent(long netPrice, long purchasePrice) =>
        netPrice == 0 ? null : decimal.Round((netPrice - purchasePrice) * 100m / netPrice, 2, MidpointRounding.AwayFromZero);

    public static decimal? MarkupPercent(long netPrice, long purchasePrice) =>
        purchasePrice == 0 ? null : decimal.Round((netPrice - purchasePrice) * 100m / purchasePrice, 2, MidpointRounding.AwayFromZero);
}
