using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;

namespace EFootwear.Tests.Catalogue;

/// <summary>Pure rules of the catalogue: barcodes, sizes, prices, the margin and markup figures of the docx examples, price bands, product header.</summary>
public class CatalogueDomainRulesTests
{
    [Fact]
    public void The_default_barcode_is_the_product_code_then_S_then_the_size_without_spaces()
    {
        Assert.Equal("5127S27", VariantRules.BuildDefaultBarcode("5127", "27"));
        Assert.Equal("5127SXL2", VariantRules.BuildDefaultBarcode("5127", "XL 2"));
    }

    [Theory]
    [InlineData("5127S27")]
    [InlineData("8901234567890")]
    [InlineData("AB-12_3/4+5")]
    public void Printable_barcodes_without_spaces_are_accepted(string barcode) => VariantRules.EnsureValidBarcode(barcode);

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("tab\there")]
    [InlineData("unicodeé")]
    public void Barcodes_with_spaces_control_or_non_ascii_characters_are_refused(string barcode)
    {
        var error = Assert.Throws<DomainException>(() => VariantRules.EnsureValidBarcode(barcode));
        Assert.Equal(ErrorKind.Validation, error.Kind);
    }

    [Fact]
    public void A_barcode_longer_than_the_limit_is_refused() =>
        Assert.Throws<DomainException>(() => VariantRules.EnsureValidBarcode(new string('1', VariantRules.BarcodeMaximumLength + 1)));

    [Theory]
    [InlineData("", false)]
    [InlineData("27", true)]
    [InlineData("XL", true)]
    public void A_size_needs_a_name(string sizeCode, bool valid)
    {
        if (valid) VariantRules.EnsureValidSizeCode(sizeCode);
        else Assert.Throws<DomainException>(() => VariantRules.EnsureValidSizeCode(sizeCode));
    }

    [Fact]
    public void The_net_price_and_margin_follow_the_docx_4_2_example()
    {
        // docx 4.2: sale price 999, discount 500 (50% rounded), net price 499, purchase price 550 gives margin -10.22.
        var net = ProductPricingFormulas.NetPrice(999, 500);
        Assert.Equal(499, net);
        Assert.Equal(-10.22m, ProductPricingFormulas.MarginPercent(net, 550));
    }

    [Fact]
    public void The_markup_is_a_different_figure_from_the_margin_and_follows_the_docx_4_10_examples()
    {
        // docx 4.10: product 1174 shows Profit % -9.27 (net 499 on cost 550) and a new price 2499 on cost 1250 shows 99.92.
        Assert.Equal(-9.27m, ProductPricingFormulas.MarkupPercent(499, 550));
        Assert.Equal(99.92m, ProductPricingFormulas.MarkupPercent(2499, 1250));
        Assert.Equal(44.94m, ProductPricingFormulas.MarginPercent(999, 550));
    }

    [Fact]
    public void A_figure_with_a_zero_divisor_is_undefined_not_an_error()
    {
        Assert.Null(ProductPricingFormulas.MarginPercent(0, 100));
        Assert.Null(ProductPricingFormulas.MarkupPercent(100, 0));
        Assert.Equal(0, ProductPricingFormulas.NetPrice(100, 250));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(CataloguePriceRules.MaximumPrice + 1)]
    public void A_price_outside_the_whole_rupee_range_is_refused(long price) =>
        Assert.Throws<DomainException>(() => CataloguePriceRules.EnsureValidPrice(price, "price"));

    [Theory]
    [InlineData(0.01)]
    [InlineData(10)]
    [InlineData(1000)]
    public void An_increase_between_a_hundredth_and_a_thousand_percent_is_accepted(double increase) =>
        CataloguePriceRules.EnsureValidIncreasePercentage((decimal)increase);

    [Theory]
    [InlineData(0)]
    [InlineData(1000.01)]
    [InlineData(5.555)]
    public void A_bad_increase_is_refused(double increase) =>
        Assert.Throws<DomainException>(() => CataloguePriceRules.EnsureValidIncreasePercentage((decimal)increase));

    [Theory]
    [InlineData(0.01)]
    [InlineData(99.99)]
    public void A_decrease_under_a_hundred_percent_is_accepted(double decrease) =>
        CataloguePriceRules.EnsureValidDecreasePercentage((decimal)decrease);

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(150)]
    public void A_decrease_of_a_hundred_percent_or_more_is_refused(double decrease) =>
        Assert.Throws<DomainException>(() => CataloguePriceRules.EnsureValidDecreasePercentage((decimal)decrease));

    [Fact]
    public void A_discount_percentage_can_allow_zero_for_removal_but_never_over_a_hundred()
    {
        CataloguePriceRules.EnsureValidPercentage(0, "discount", allowZero: true);
        Assert.Throws<DomainException>(() => CataloguePriceRules.EnsureValidPercentage(0, "discount"));
        Assert.Throws<DomainException>(() => CataloguePriceRules.EnsureValidPercentage(100.01m, "discount"));
    }

    [Fact]
    public void A_price_band_needs_a_name_and_a_maximum_that_is_not_below_the_minimum()
    {
        PriceBandRules.EnsureValid("Budget", 0, 999, 1);
        PriceBandRules.EnsureValid("Premium", 5000, null, 2);
        Assert.Throws<DomainException>(() => PriceBandRules.EnsureValid("", 0, 10, 0));
        Assert.Throws<DomainException>(() => PriceBandRules.EnsureValid("Bad", 500, 100, 0));
    }

    [Fact]
    public void A_product_header_is_checked_for_name_weight_tax_and_status()
    {
        ProductRules.EnsureValidHeader("Sandal", null, null, 0.5m, 17m, null, ProductStatusCodes.Active);
        Assert.Throws<DomainException>(() => ProductRules.EnsureValidHeader("", null, null, null, 0, null, ProductStatusCodes.Active));
        Assert.Throws<DomainException>(() => ProductRules.EnsureValidHeader("Sandal", null, null, -1, 0, null, ProductStatusCodes.Active));
        Assert.Throws<DomainException>(() => ProductRules.EnsureValidHeader("Sandal", null, null, null, 101, null, ProductStatusCodes.Active));
        Assert.Throws<DomainException>(() => ProductRules.EnsureValidHeader("Sandal", null, null, null, 0, null, "GONE"));
    }

    [Fact]
    public void Image_slots_run_from_one_to_four()
    {
        ProductImageRules.EnsureValidSlot(1);
        ProductImageRules.EnsureValidSlot(ProductImageRules.SlotCount);
        Assert.Throws<DomainException>(() => ProductImageRules.EnsureValidSlot(0));
        Assert.Throws<DomainException>(() => ProductImageRules.EnsureValidSlot(ProductImageRules.SlotCount + 1));
    }

    [Fact]
    public void The_attribute_field_codes_include_the_docx_fields_and_the_colour_improvement()
    {
        Assert.True(ProductAttributeFieldCodes.IsKnown(ProductAttributeFieldCodes.Unit));
        Assert.True(ProductAttributeFieldCodes.IsKnown(ProductAttributeFieldCodes.Brand));
        Assert.True(ProductAttributeFieldCodes.IsKnown(ProductAttributeFieldCodes.Color));
        Assert.False(ProductAttributeFieldCodes.IsKnown("PASSWORD"));
        Assert.False(ProductAttributeFieldCodes.IsKnown(null));
    }
}
