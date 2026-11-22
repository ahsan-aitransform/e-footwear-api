namespace EFootwear.Domain.Accounting;

/// <summary>Status of a month in <c>acct.periods</c>. A closed month refuses new journal entries dated in it (trigger acct.check_period_open).</summary>
public static class AccountingPeriodStatusCodes
{
    public const string Open = "OPEN";
    public const string Closed = "CLOSED";
}
