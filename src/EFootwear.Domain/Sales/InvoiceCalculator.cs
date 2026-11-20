using EFootwear.Domain.Common;

namespace EFootwear.Domain.Sales;

/// <summary>One sale line as the cashier built it: what the shelf says, what is charged, how many.</summary>
public sealed record CalcLine(decimal Qty, long ListPrice, long UnitPrice, decimal TaxRate);

public sealed record CalcLineResult(long Gross, long LineDiscount, long BillDiscountShare, long Taxable, long Tax, long Net)
{
    /// <summary>sales.invoice_lines.discount_amount: what was taken off the list price, line level and bill share together.</summary>
    public long DiscountAmount => LineDiscount + BillDiscountShare;
}

public sealed record InvoiceTotals(long Gross, long LineDiscount, long BillDiscount, long Taxable, long Tax, long Net, IReadOnlyList<CalcLineResult> Lines);

/// <summary>
/// Totals of a sale, in whole rupees (v6 stores money as bigint). Rules:
/// <list type="bullet">
/// <item>a line is worth round(unit price × qty); the discount is list − charged (never negative: a price raised above list has no discount);</item>
/// <item>a bill discount is spread over the lines in proportion to their value, the last line takes the remainder, so the parts add up exactly;</item>
/// <item>tax is added on top of the discounted value (prices are tax-exclusive); the default tax rate is 0.</item>
/// </list>
/// The database refuses an invoice whose header differs from the sum of its lines, so these sums are the single source of truth.
/// </summary>
public static class InvoiceCalculator
{
    public static InvoiceTotals Sale(IReadOnlyList<CalcLine> lines, long billDiscount, string roundingMode)
    {
        if (lines.Count == 0) throw new ValidationException("lines", "Add at least one item.");
        if (billDiscount < 0) throw new ValidationException("billDiscount", "Discount cannot be negative.");

        var after = new long[lines.Count];
        var gross = new long[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Qty <= 0) throw new ValidationException($"lines[{i}].qty", "Quantity must be above zero.");
            after[i] = Money.Round(l.UnitPrice * l.Qty, roundingMode);
            gross[i] = Math.Max(Money.Round(l.ListPrice * l.Qty, roundingMode), after[i]);
        }

        var pool = after.Sum();
        if (billDiscount > pool) throw new ValidationException("billDiscount", "The bill discount is more than the bill.");
        var shares = new long[lines.Count];
        if (billDiscount > 0 && pool > 0)
        {
            long given = 0; var last = Array.FindLastIndex(after, a => a > 0);
            for (var i = 0; i < lines.Count; i++)
            {
                shares[i] = i == last ? billDiscount - given : (long)Math.Floor(billDiscount * (decimal)after[i] / pool);
                given += shares[i];
            }
        }

        var results = new List<CalcLineResult>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var taxable = after[i] - shares[i];
            var tax = Money.Round(taxable * lines[i].TaxRate / 100m, roundingMode);
            results.Add(new CalcLineResult(gross[i], gross[i] - after[i], shares[i], taxable, tax, taxable + tax));
        }
        return new InvoiceTotals(results.Sum(r => r.Gross), results.Sum(r => r.LineDiscount), billDiscount, results.Sum(r => r.Taxable),
            results.Sum(r => r.Tax), results.Sum(r => r.Net), results);
    }

    /// <summary>The share of an original line that comes back: every amount in proportion to the quantity returned (all results are negative).</summary>
    public static CalcLineResult ReturnShare(decimal returnQty, decimal soldQty, long gross, long discount, long taxable, long tax)
    {
        if (returnQty <= 0 || returnQty > soldQty) throw new ValidationException("qty", "Return quantity must be above zero and not more than was sold.");
        var f = returnQty / soldQty;
        long P(long v) => returnQty == soldQty ? v : (long)Math.Round(v * f, 0, MidpointRounding.AwayFromZero);
        var t = P(taxable); var x = P(tax);
        return new CalcLineResult(-P(gross), -P(discount), 0, -t, -x, -(t + x));
    }
}
