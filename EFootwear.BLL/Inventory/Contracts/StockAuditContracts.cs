namespace EFootwear.BLL.Inventory.Contracts;

public sealed record CreateStockAuditRequest(Guid StoreId, DateOnly AuditDate, Guid ClientTransactionId, string? Note);

/// <summary>One scan of the count. The scan id is made by the screen: sending it again changes nothing. A negative quantity corrects an over-count.</summary>
public sealed record RecordStockAuditScanRequest(string? Barcode, int Quantity, Guid DeviceScanId);

public sealed record StockAuditScanResultResponse(Guid VariantId, string ProductName, string SizeCode, decimal CountedQuantity, bool WasAlreadyRecorded);

/// <summary>"Adjust Stock" of the audit report. IncludeUnscannedItems books zero for sizes with stock that were not scanned ("Add Non-Scanned Items").</summary>
public sealed record PostStockAuditRequest(bool IncludeUnscannedItems);

public sealed record StockAuditLineResponse(
    Guid VariantId,
    string Barcode,
    string ProductName,
    string? CategoryName,
    string? BrandName,
    string? GenderName,
    string? SeasonName,
    string SizeCode,
    decimal ExpectedQuantity,
    decimal CountedQuantity,
    decimal DifferenceQuantity,
    long PurchasePriceAmount,
    long SalePriceAmount,
    long DifferenceAmount,
    bool WasScanned);

public sealed record StockAuditSummaryResponse(int CountedItemCount, int MoreItemCount, int LessItemCount, int MatchedItemCount, long DifferenceAmount);

/// <summary>The audit with its comparison. While the audit is being counted the book quantity is the stock now; after posting it is the quantity the posting used.</summary>
public sealed record StockAuditResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    long DocumentNumber,
    DateOnly AuditDate,
    string StatusCode,
    string? Note,
    bool IncludesUnscannedItems,
    DateTimeOffset? PostedAt,
    StockAuditSummaryResponse Summary,
    IReadOnlyList<StockAuditLineResponse> Lines);

public sealed record StockAuditListItemResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    long DocumentNumber,
    DateOnly AuditDate,
    string StatusCode,
    string? Note,
    int ScannedPieceCount,
    int ScannedItemCount);

public sealed record StockAuditSearchFilter(Guid? StoreId, DateOnly? FromDate, DateOnly? ToDate, string? StatusCode);
