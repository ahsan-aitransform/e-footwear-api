namespace EFootwear.DAL.Inventory.Repositories;

// Row shapes of the raw SQL of InventoryReadRepository: one property per selected column, named exactly like its "Alias".

internal sealed class StoreRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class ItemRow
{
    public Guid VariantId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? ProductNameUrdu { get; set; }
    public string? UnitName { get; set; }
    public string SizeCode { get; set; } = "";
    public string? ColorName { get; set; }
    public string Barcode { get; set; } = "";
    public decimal StockQuantity { get; set; }
    public long PurchasePriceAmount { get; set; }
}

internal sealed class NonZeroStockRow
{
    public Guid VariantId { get; set; }
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string SizeCode { get; set; } = "";
    public long PurchasePriceAmount { get; set; }
    public decimal StockQuantity { get; set; }
}

internal sealed class StockQuantityRow
{
    public Guid VariantId { get; set; }
    public decimal Quantity { get; set; }
}

internal sealed class AdjustmentHeaderRow
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = "";
    public long DocumentNumber { get; set; }
    public DateOnly DocumentDate { get; set; }
    public string StatusCode { get; set; } = "";
    public string KindCode { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ReversesDocumentId { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public decimal AddedQuantity { get; set; }
    public decimal RemovedQuantity { get; set; }
    public long NetValueAmount { get; set; }
    public int LineCount { get; set; }
}

internal sealed class AdjustmentLineRow
{
    public int LineNumber { get; set; }
    public Guid VariantId { get; set; }
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? UnitName { get; set; }
    public string SizeCode { get; set; } = "";
    public decimal? ExpectedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public decimal Quantity { get; set; }
    public long UnitCostAmount { get; set; }
    public string? ReasonText { get; set; }
}

internal sealed class AuditHeaderRow
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = "";
    public long DocumentNumber { get; set; }
    public DateOnly DocumentDate { get; set; }
    public string StatusCode { get; set; } = "";
    public string? Note { get; set; }
    public bool IncludesUnscannedItems { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
}

internal sealed class AuditListRow
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = "";
    public long DocumentNumber { get; set; }
    public DateOnly DocumentDate { get; set; }
    public string StatusCode { get; set; } = "";
    public string? Note { get; set; }
    public int ScannedPieceCount { get; set; }
    public int ScannedItemCount { get; set; }
}

internal sealed class AuditLineRow
{
    public Guid VariantId { get; set; }
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? CategoryName { get; set; }
    public string? BrandName { get; set; }
    public string? GenderName { get; set; }
    public string? SeasonName { get; set; }
    public string SizeCode { get; set; } = "";
    public decimal ExpectedQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public long PurchasePriceAmount { get; set; }
    public long SalePriceAmount { get; set; }
    public bool WasScanned { get; set; }
}

internal sealed class ClaimRow
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = "";
    public long ClaimNumber { get; set; }
    public DateOnly ClaimDate { get; set; }
    public Guid VariantId { get; set; }
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string SizeCode { get; set; } = "";
    public string? BrandName { get; set; }
    public string? GenderName { get; set; }
    public string? SeasonName { get; set; }
    public decimal Quantity { get; set; }
    public long PurchasePriceAmount { get; set; }
    public string ReasonCode { get; set; } = "";
    public string? Note { get; set; }
    public string StatusCode { get; set; } = "";
    public string? RequestedByName { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? RejectReason { get; set; }
}

internal sealed class TransferHeaderRow
{
    public Guid Id { get; set; }
    public long TransferNumber { get; set; }
    public Guid FromStoreId { get; set; }
    public string FromStoreName { get; set; } = "";
    public Guid ToStoreId { get; set; }
    public string ToStoreName { get; set; } = "";
    public string StatusCode { get; set; } = "";
    public string? Note { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal QuantityScanned { get; set; }
    public decimal QuantityShipped { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal QuantityMissing { get; set; }
}

internal sealed class TransferLineRow
{
    public Guid LineId { get; set; }
    public Guid VariantId { get; set; }
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string SizeCode { get; set; } = "";
    public decimal QuantityScanned { get; set; }
    public decimal QuantityShipped { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal QuantityMissing { get; set; }
    public long UnitCostAmount { get; set; }
}

internal sealed class LossCaseRow
{
    public Guid Id { get; set; }
    public Guid TransferId { get; set; }
    public long TransferNumber { get; set; }
    public Guid FromStoreId { get; set; }
    public string FromStoreName { get; set; } = "";
    public Guid ToStoreId { get; set; }
    public string ToStoreName { get; set; } = "";
    public Guid VariantId { get; set; }
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string SizeCode { get; set; } = "";
    public decimal Quantity { get; set; }
    public string? Reason { get; set; }
    public string? BearerCode { get; set; }
    public string StatusCode { get; set; } = "";
    public DateTimeOffset RaisedAt { get; set; }
}

internal sealed class AdjustmentReportRow
{
    public string StoreName { get; set; } = "";
    public long DocumentNumber { get; set; }
    public DateOnly DocumentDate { get; set; }
    public string KindCode { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string SizeCode { get; set; } = "";
    public decimal Quantity { get; set; }
    public long UnitCostAmount { get; set; }
}

internal sealed class TransferReportRow
{
    public Guid TransferId { get; set; }
    public long TransferNumber { get; set; }
    public string FromStoreName { get; set; } = "";
    public string ToStoreName { get; set; } = "";
    public string StatusCode { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string SizeCode { get; set; } = "";
    public decimal QuantityShipped { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal QuantityMissing { get; set; }
    public long UnitCostAmount { get; set; }
}

internal sealed class TransferSummaryRow
{
    public string FromStoreName { get; set; } = "";
    public string ToStoreName { get; set; } = "";
    public int TransferCount { get; set; }
    public decimal QuantityShipped { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal QuantityMissing { get; set; }
    public long ValueAmount { get; set; }
}

internal sealed class MismatchRow
{
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = "";
    public Guid VariantId { get; set; }
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string SizeCode { get; set; } = "";
    public decimal BalanceQuantity { get; set; }
    public decimal LedgerQuantity { get; set; }
}

internal sealed class ReconciliationTotalsRow
{
    public int CheckedSizeCount { get; set; }
    public int NegativeBalanceCount { get; set; }
    public long StockValueAmount { get; set; }
}

internal sealed class InTransitRow
{
    public int TransferCount { get; set; }
    public decimal PieceCount { get; set; }
    public int OpenLossCaseCount { get; set; }
}
