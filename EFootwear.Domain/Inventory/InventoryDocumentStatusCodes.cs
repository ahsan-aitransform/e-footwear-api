namespace EFootwear.Domain.Inventory;

/// <summary>The statuses of inv.documents (column status). An adjustment is POSTED or, once voided, CANCELLED; an audit is DRAFT while it is counted, then POSTED.</summary>
public static class InventoryDocumentStatusCodes
{
    public const string Draft = "DRAFT";
    public const string Posted = "POSTED";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";

    public static bool IsValid(string? code) => code is Draft or Posted or Approved or Rejected or Cancelled;
}
