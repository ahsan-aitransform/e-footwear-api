namespace EFootwear.Domain.Commissions;

/// <summary>
/// The life of a commission entry (hr.commission_entries.status): PENDING when the sale is posted, APPROVED by a person who may approve commission,
/// PAID when the salary sheet line that took it is paid. Nothing here pays commission by itself: payment is the salary payment of the payroll module.
/// </summary>
public static class CommissionEntryStatusCodes
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string OnHold = "ON_HOLD";
    public const string Paid = "PAID";
    public const string Cancelled = "CANCELLED";

    public static readonly IReadOnlyList<string> All = [Pending, Approved, OnHold, Paid, Cancelled];

    public static bool IsValid(string? statusCode) => statusCode is Pending or Approved or OnHold or Paid or Cancelled;
}
