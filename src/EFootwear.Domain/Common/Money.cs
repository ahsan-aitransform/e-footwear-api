namespace EFootwear.Domain.Common;

/// <summary>
/// v6 stores money as whole rupees (<c>core.money</c> = bigint). A computed customer-facing amount is rounded with the
/// company's rounding mode (platform.companies.rounding_mode): FLOOR drops the paisa, NEAREST rounds half up.
/// The database function core.round_money does the same; this mirror lets the API compute totals before it inserts them.
/// </summary>
public static class Money
{
    public static long Round(decimal amount, string roundingMode)
        => roundingMode == "NEAREST" ? (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero) : (long)Math.Floor(amount);

    public static decimal RoundQty(decimal v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);
    public static decimal RoundCost(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}
