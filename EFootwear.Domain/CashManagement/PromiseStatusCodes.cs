namespace EFootwear.Domain.CashManagement;

/// <summary>Status of a customer promise. The values mirror the CHECK constraint of cash.promises.</summary>
public static class PromiseStatusCodes
{
    public const string Open = "OPEN";
    public const string Partial = "PARTIAL";
    public const string Paid = "PAID";
    public const string Broken = "BROKEN";
    public const string Cancelled = "CANCELLED";

    /// <summary>A promise can still be changed or cancelled while it is open or partly paid.</summary>
    public static bool IsStillActive(string statusCode) => statusCode is Open or Partial;
}
