namespace EFootwear.Domain.Catalogue;

/// <summary>One barcode replacement of one variant. Table: catalog.barcode_history, append-only (migration 0300).</summary>
public sealed class BarcodeHistoryEntry
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public string OldBarcode { get; set; } = "";
    public string NewBarcode { get; set; } = "";
    public Guid? BatchId { get; set; }
    public string? Reason { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
