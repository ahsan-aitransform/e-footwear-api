namespace EFootwear.BLL.Inventory.Contracts;

/// <summary>One line of a stock adjustment: a signed quantity (plus adds, minus removes). The unit cost is optional; the store's average cost is used when it is empty.</summary>
public sealed record StockAdjustmentLineRequest(Guid VariantId, decimal Quantity, long? UnitCostAmount, string? ReasonText);

/// <summary>Stock Adjustment (docx 5.7). The client transaction id makes a repeated send harmless: the same document comes back.</summary>
public sealed record CreateStockAdjustmentRequest(
    Guid StoreId,
    DateOnly AdjustmentDate,
    Guid ClientTransactionId,
    string? Description,
    IReadOnlyList<StockAdjustmentLineRequest> Lines);

public sealed record StockCountLineRequest(Guid VariantId, decimal CountedQuantity);

/// <summary>The counted stock typed into the non-zero stock list (docx 5.8). Only lines whose count differs from the books are posted.</summary>
public sealed record CreateStockCountUpdateRequest(
    Guid StoreId,
    DateOnly UpdateDate,
    Guid ClientTransactionId,
    string? Description,
    IReadOnlyList<StockCountLineRequest> Lines);

public sealed record VoidStockAdjustmentRequest(string? Reason, Guid ClientTransactionId);

public sealed record StockAdjustmentLineResponse(
    int LineNumber,
    Guid VariantId,
    string Barcode,
    string ProductName,
    string? UnitName,
    string SizeCode,
    decimal? ExpectedQuantity,
    decimal? CountedQuantity,
    decimal Quantity,
    long UnitCostAmount,
    string? ReasonText);

public sealed record StockAdjustmentResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    long DocumentNumber,
    DateOnly AdjustmentDate,
    string StatusCode,
    string KindCode,
    string? Description,
    Guid? ReversesDocumentId,
    string? CreatedByName,
    DateTimeOffset? PostedAt,
    decimal AddedQuantity,
    decimal RemovedQuantity,
    long NetValueAmount,
    IReadOnlyList<StockAdjustmentLineResponse> Lines);

/// <summary>A void keeps the original (now cancelled) and adds a reversal document that undoes it.</summary>
public sealed record VoidStockAdjustmentResponse(StockAdjustmentResponse Original, StockAdjustmentResponse Reversal);

public sealed record StockAdjustmentListItemResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    long DocumentNumber,
    DateOnly AdjustmentDate,
    string StatusCode,
    string KindCode,
    string? Description,
    int LineCount,
    decimal AddedQuantity,
    decimal RemovedQuantity,
    long NetValueAmount);

public sealed record StockAdjustmentSearchFilter(Guid? StoreId, DateOnly? FromDate, DateOnly? ToDate, string? StatusCode, string? KindCode);

/// <summary>What the count file (.xlsx or .csv) contained: rows that matched a size of the store, and rows that could not be used, with the reason.</summary>
public sealed record StockCountFilePreviewResponse(
    IReadOnlyList<StockCountFileRowResponse> Rows,
    IReadOnlyList<StockCountFileProblemResponse> Problems);

public sealed record StockCountFileRowResponse(Guid VariantId, string Barcode, string ProductName, string SizeCode, decimal BookQuantity, decimal CountedQuantity);

public sealed record StockCountFileProblemResponse(int RowNumber, string Barcode, string Message);
