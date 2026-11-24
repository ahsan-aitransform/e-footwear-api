namespace EFootwear.BLL.Inventory.Contracts;

public sealed record StockAdjustmentReportRowResponse(
    string StoreName,
    long DocumentNumber,
    DateOnly AdjustmentDate,
    string KindCode,
    string Barcode,
    string ProductName,
    string SizeCode,
    decimal AddedQuantity,
    decimal RemovedQuantity,
    long UnitCostAmount,
    long ValueAmount);

public sealed record StockAdjustmentReportResponse(
    IReadOnlyList<StockAdjustmentReportRowResponse> Rows,
    decimal TotalAddedQuantity,
    decimal TotalRemovedQuantity,
    long NetValueAmount);

public sealed record StockTransferReportRowResponse(
    Guid TransferId,
    long TransferNumber,
    string FromStoreName,
    string ToStoreName,
    string StatusCode,
    DateTimeOffset CreatedAt,
    string Barcode,
    string ProductName,
    string SizeCode,
    decimal QuantityShipped,
    decimal QuantityReceived,
    decimal QuantityMissing,
    long ValueAmount);

public sealed record StockTransferReportResponse(
    IReadOnlyList<StockTransferReportRowResponse> Rows,
    decimal TotalQuantityShipped,
    decimal TotalQuantityReceived,
    decimal TotalQuantityMissing,
    long TotalValueAmount);

public sealed record StockTransferSummaryRowResponse(
    string FromStoreName,
    string ToStoreName,
    int TransferCount,
    decimal QuantityShipped,
    decimal QuantityReceived,
    decimal QuantityMissing,
    long ValueAmount);

public sealed record StockTransferSummaryResponse(
    IReadOnlyList<StockTransferSummaryRowResponse> Rows,
    int TotalTransferCount,
    decimal TotalQuantityShipped,
    decimal TotalQuantityReceived,
    decimal TotalQuantityMissing,
    long TotalValueAmount);

public sealed record ProductClaimReportResponse(
    IReadOnlyList<ProductClaimResponse> Rows,
    decimal TotalQuantity,
    long TotalValueAmount);

/// <summary>Scope of the Transfer Report (docx 8.21): ALL, BY_TRANSFER (one number) or BY_STORE.</summary>
public sealed record StockTransferReportFilter(string ScopeCode, long? TransferNumber, Guid? StoreId, DateOnly? FromDate, DateOnly? ToDate);
