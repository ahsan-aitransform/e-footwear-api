namespace EFootwear.BLL.Inventory.Abstractions;

/// <summary>One line handed to the stock posting functions. Quantity is signed (plus adds, minus removes); the expected and counted quantities are only filled for a count update.</summary>
public sealed record StockMovementLine(Guid VariantId, decimal Quantity, long? UnitCostAmount, string? ReasonText, decimal? ExpectedQuantity, decimal? CountedQuantity);

public sealed record AuditScanOutcome(Guid VariantId, decimal CountedQuantity, bool WasAlreadyRecorded);

public sealed record TransferScanOutcome(Guid VariantId, decimal QuantityScanned, decimal QuantityReceived, bool WasAlreadyRecorded);

/// <summary>
/// Writes of the Inventory module. Each method calls one database function of migration 0400 (or the baseline): the functions number the document, lock and
/// move the stock, post the journal and check the permission again. A call is one step of a transaction that the service commits.
/// </summary>
public interface IInventoryPostingRepository
{
    Task<Guid> PostAdjustmentAsync(Guid storeId, DateOnly adjustmentDate, Guid clientTransactionId, string? description, IReadOnlyList<StockMovementLine> lines, string kindCode, CancellationToken cancellationToken);

    Task<Guid> VoidAdjustmentAsync(Guid documentId, string reason, Guid clientTransactionId, CancellationToken cancellationToken);

    Task<Guid> CreateAuditAsync(Guid storeId, DateOnly auditDate, Guid clientTransactionId, string? note, CancellationToken cancellationToken);

    Task<AuditScanOutcome> RecordAuditScanAsync(Guid auditId, string barcode, int quantity, Guid deviceScanId, CancellationToken cancellationToken);

    Task<Guid> PostAuditAsync(Guid auditId, bool includeUnscannedItems, CancellationToken cancellationToken);

    Task CancelAuditAsync(Guid auditId, CancellationToken cancellationToken);

    Task<Guid> RequestClaimAsync(Guid storeId, DateOnly claimDate, Guid clientTransactionId, Guid variantId, decimal quantity, string reasonCode, string? note, CancellationToken cancellationToken);

    Task ApproveClaimAsync(Guid claimId, CancellationToken cancellationToken);

    Task RejectClaimAsync(Guid claimId, string reason, CancellationToken cancellationToken);

    Task<Guid> CreateTransferAsync(Guid fromStoreId, Guid toStoreId, Guid clientTransactionId, string? note, CancellationToken cancellationToken);

    Task<TransferScanOutcome> RecordTransferScanAsync(Guid transferId, Guid storeId, string phaseCode, string barcode, int quantity, Guid scanBatchId, CancellationToken cancellationToken);

    Task ShipTransferAsync(Guid transferId, CancellationToken cancellationToken);

    Task CloseTransferAsync(Guid transferId, CancellationToken cancellationToken);

    Task CancelTransferAsync(Guid transferId, string? reason, CancellationToken cancellationToken);

    Task DecideLossCaseAsync(Guid lossCaseId, string decisionCode, string? bearerCode, CancellationToken cancellationToken);
}
