namespace EFootwear.Domain.Inventory;

/// <summary>The statuses of inv.transfers (column status). DRAFT: pieces are being scanned for sending. SHIPPED: left the sender. RECEIVING: the receiver has begun scanning.</summary>
public static class StockTransferStatusCodes
{
    public const string Draft = "DRAFT";
    public const string Shipped = "SHIPPED";
    public const string Receiving = "RECEIVING";
    public const string Completed = "COMPLETED";
    public const string CompletedWithMissing = "COMPLETED_WITH_MISSING";
    public const string Cancelled = "CANCELLED";

    public static bool IsValid(string? code) => code is Draft or Shipped or Receiving or Completed or CompletedWithMissing or Cancelled;

    /// <summary>A transfer the receiving store can still work on.</summary>
    public static bool CanBeReceived(string? code) => code is Shipped or Receiving;
}
