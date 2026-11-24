namespace EFootwear.BLL.Inventory.Contracts;

/// <summary>Stock Transfer (docx 5.9). It starts as a draft with no pieces: the sending store adds pieces by scanning (Part 19).</summary>
public sealed record CreateStockTransferRequest(Guid FromStoreId, Guid ToStoreId, Guid ClientTransactionId, string? Note);

/// <summary>One scan of a transfer: the sending store scans while it is a draft, the receiving store scans after dispatch. The scan id is made by the screen.</summary>
public sealed record RecordTransferScanRequest(string? Barcode, int Quantity, Guid ScanBatchId);

public sealed record TransferScanResultResponse(Guid VariantId, string ProductName, string SizeCode, decimal QuantityScanned, decimal QuantityReceived, bool WasAlreadyRecorded);

public sealed record CancelStockTransferRequest(string? Reason);

public sealed record StockTransferLineResponse(
    Guid LineId,
    Guid VariantId,
    string Barcode,
    string ProductName,
    string SizeCode,
    decimal QuantityScanned,
    decimal QuantityShipped,
    decimal QuantityReceived,
    decimal QuantityMissing,
    long UnitCostAmount);

public sealed record TransferLossCaseResponse(
    Guid Id,
    Guid TransferId,
    long TransferNumber,
    Guid FromStoreId,
    string FromStoreName,
    Guid ToStoreId,
    string ToStoreName,
    Guid VariantId,
    string Barcode,
    string ProductName,
    string SizeCode,
    decimal Quantity,
    string? Reason,
    string? BearerCode,
    string StatusCode,
    DateTimeOffset RaisedAt);

public sealed record StockTransferResponse(
    Guid Id,
    long TransferNumber,
    Guid FromStoreId,
    string FromStoreName,
    Guid ToStoreId,
    string ToStoreName,
    string StatusCode,
    string? Note,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? ClosedAt,
    decimal QuantityScanned,
    decimal QuantityShipped,
    decimal QuantityReceived,
    decimal QuantityMissing,
    IReadOnlyList<StockTransferLineResponse> Lines,
    IReadOnlyList<TransferLossCaseResponse> LossCases);

public sealed record StockTransferListItemResponse(
    Guid Id,
    long TransferNumber,
    Guid FromStoreId,
    string FromStoreName,
    Guid ToStoreId,
    string ToStoreName,
    string StatusCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? ClosedAt,
    decimal QuantityScanned,
    decimal QuantityShipped,
    decimal QuantityReceived,
    decimal QuantityMissing);

/// <summary>DirectionCode is seen from StoreId: OUTGOING sent by it, INCOMING received by it, ALL both. Without a store every transfer the user may see is listed.</summary>
public sealed record StockTransferSearchFilter(Guid? StoreId, string? DirectionCode, string? StatusCode, DateOnly? FromDate, DateOnly? ToDate, long? TransferNumber, bool OnlySentTransfers = false);

public sealed record MultiTransferLineRequest(string? Barcode, int Quantity);

public sealed record MultiTransferDestinationRequest(Guid ToStoreId, Guid ClientTransactionId, IReadOnlyList<MultiTransferLineRequest> Lines);

/// <summary>Stock Transfer Multi (docx 5.10): one sending store, several receiving stores, each with its own pieces. Every destination becomes its own transfer; with Dispatch they are dispatched together.</summary>
public sealed record CreateMultiStockTransferRequest(
    Guid FromStoreId,
    string? Note,
    bool Dispatch,
    IReadOnlyList<MultiTransferDestinationRequest> Destinations);

public sealed record CreateMultiStockTransferResponse(IReadOnlyList<StockTransferResponse> Transfers);

/// <summary>Deciding missing pieces: FOUND (they go into the receiver's stock) or CHARGE (the loss goes to the bearer). No default bearer exists.</summary>
public sealed record DecideTransferLossRequest(string DecisionCode, string? BearerCode);
