namespace EFootwear.Domain.Accounting;

/// <summary>
/// One calendar month of a company's books. Table: acct.periods. A month without a row is open. Closing records who closed it and when;
/// who may close a month and when is a business policy that the schema does not define ([BUSINESS DECISION REQUIRED]), so nothing
/// closes a month automatically.
/// </summary>
public sealed class AccountingPeriod
{
    public Guid CompanyId { get; set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly PeriodStart { get; set; }

    public string Status { get; set; } = AccountingPeriodStatusCodes.Open;
    public Guid? ClosedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
