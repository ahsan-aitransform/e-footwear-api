using EFootwear.Domain.Common;
using EFootwear.Domain.Sales;
using Xunit;

namespace EFootwear.Api.Tests;

public sealed class InvoiceCalculatorTests
{
    [Fact]
    public void Plain_sale_has_no_discount_and_adds_up()
    {
        var t = InvoiceCalculator.Sale([new(2, 1000, 1000, 0), new(1, 500, 500, 0)], 0, "FLOOR");
        Assert.Equal((2500, 0, 0, 2500, 0, 2500), (t.Gross, t.LineDiscount, t.BillDiscount, t.Taxable, t.Tax, t.Net));
    }

    [Fact]
    public void Promotion_price_is_a_line_discount_and_a_raised_price_is_not_a_negative_discount()
    {
        var t = InvoiceCalculator.Sale([new(1, 2000, 1800, 0), new(1, 1000, 1200, 0)], 0, "FLOOR");
        Assert.Equal(200, t.Lines[0].LineDiscount);
        Assert.Equal(0, t.Lines[1].LineDiscount);
        Assert.Equal(1200, t.Lines[1].Gross);
        Assert.Equal(3000, t.Net);
    }

    [Fact]
    public void Bill_discount_is_spread_exactly_with_the_remainder_on_the_last_line()
    {
        var t = InvoiceCalculator.Sale([new(1, 1000, 1000, 0), new(1, 1000, 1000, 0), new(1, 1000, 1000, 0)], 100, "FLOOR");
        Assert.Equal(100, t.Lines.Sum(l => l.BillDiscountShare));
        Assert.Equal([33, 33, 34], t.Lines.Select(l => l.BillDiscountShare).ToArray());
        Assert.Equal(2900, t.Net);
        Assert.Equal(2900, t.Lines.Sum(l => l.Net));
    }

    [Fact]
    public void Tax_is_added_on_top_of_the_discounted_value()
    {
        var t = InvoiceCalculator.Sale([new(1, 1000, 1000, 17)], 100, "FLOOR");
        Assert.Equal(900, t.Taxable);
        Assert.Equal(153, t.Tax);          // 900 × 17% = 153
        Assert.Equal(1053, t.Net);
    }

    [Theory]
    [InlineData("FLOOR", 1039)]
    [InlineData("NEAREST", 1040)]
    public void Fractional_quantities_round_by_the_company_rule(string mode, long expected)
        => Assert.Equal(expected, InvoiceCalculator.Sale([new(1.5m, 693, 693, 0)], 0, mode).Net);

    [Fact]
    public void Refusals()
    {
        Assert.Throws<ValidationException>(() => InvoiceCalculator.Sale([], 0, "FLOOR"));
        Assert.Throws<ValidationException>(() => InvoiceCalculator.Sale([new(0, 1, 1, 0)], 0, "FLOOR"));
        Assert.Throws<ValidationException>(() => InvoiceCalculator.Sale([new(1, 100, 100, 0)], 101, "FLOOR"));
        Assert.Throws<ValidationException>(() => InvoiceCalculator.Sale([new(1, 100, 100, 0)], -1, "FLOOR"));
    }

    [Fact]
    public void Return_share_is_proportional_negative_and_exact_for_the_full_quantity()
    {
        var full = InvoiceCalculator.ReturnShare(3, 3, 3000, 300, 2700, 459);
        Assert.Equal((-3000, -300, -2700, -459, -3159), (full.Gross, full.LineDiscount, full.Taxable, full.Tax, full.Net));
        var one = InvoiceCalculator.ReturnShare(1, 3, 3000, 300, 2700, 459);
        Assert.Equal((-1000, -100, -900, -153, -1053), (one.Gross, one.LineDiscount, one.Taxable, one.Tax, one.Net));
        Assert.Throws<ValidationException>(() => InvoiceCalculator.ReturnShare(4, 3, 1, 1, 1, 1));
    }
}
