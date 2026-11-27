using EFootwear.BLL.Common;
using EFootwear.BLL.Inventory.Contracts;

namespace EFootwear.BLL.Inventory.Abstractions;

/// <summary>Reads of the Inventory module. Plain SQL; row-level security already limits every answer to the company and the stores of the user.</summary>
public interface IInventoryReadRepository
{
    // lookups
    Task<IReadOnlyList<InventoryStoreResponse>> ListStoresAsync(CancellationToken cancellationToken);
    Task<InventoryItemResponse?> FindItemByBarcodeAsync(Guid storeId, string barcode, CancellationToken cancellationToken);
    /// <summary>The stored barcode that a typed or scanned text means (capital letters do not matter); the text itself when no single size matches.</summary>
    Task<string> FindStoredBarcodeAsync(string barcode, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryItemResponse>> SearchItemsAsync(Guid storeId, string searchText, int limit, CancellationToken cancellationToken);
    Task<PagedResult<NonZeroStockRowResponse>> ListNonZeroStockAsync(Guid storeId, PageRequest pageRequest, CancellationToken cancellationToken);
    /// <summary>The books' quantity of each size in the store (zero when there is no balance row yet).</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> FindStockQuantitiesAsync(Guid storeId, IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryItemResponse>> FindItemsByBarcodesAsync(Guid storeId, IReadOnlyCollection<string> barcodes, CancellationToken cancellationToken);

    // adjustments
    Task<StockAdjustmentResponse?> FindAdjustmentAsync(Guid documentId, CancellationToken cancellationToken);
    Task<PagedResult<StockAdjustmentListItemResponse>> SearchAdjustmentsAsync(StockAdjustmentSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken);

    // audits
    Task<StockAuditResponse?> FindAuditAsync(Guid auditId, bool includeUnscannedItems, CancellationToken cancellationToken);
    Task<PagedResult<StockAuditListItemResponse>> SearchAuditsAsync(StockAuditSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken);

    // claims
    Task<ProductClaimResponse?> FindClaimAsync(Guid claimId, CancellationToken cancellationToken);
    Task<PagedResult<ProductClaimResponse>> SearchClaimsAsync(ProductClaimSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<ProductClaimReportResponse> BuildClaimReportAsync(ProductClaimSearchFilter filter, CancellationToken cancellationToken);

    // transfers
    Task<StockTransferResponse?> FindTransferAsync(Guid transferId, CancellationToken cancellationToken);
    Task<PagedResult<StockTransferListItemResponse>> SearchTransfersAsync(StockTransferSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<TransferLossCaseResponse?> FindLossCaseAsync(Guid lossCaseId, CancellationToken cancellationToken);
    Task<PagedResult<TransferLossCaseResponse>> SearchLossCasesAsync(string? statusCode, PageRequest pageRequest, CancellationToken cancellationToken);

    // reports
    Task<StockAdjustmentReportResponse> BuildAdjustmentReportAsync(StockAdjustmentSearchFilter filter, CancellationToken cancellationToken);
    Task<StockTransferReportResponse> BuildTransferReportAsync(StockTransferReportFilter filter, CancellationToken cancellationToken);
    Task<StockTransferSummaryResponse> BuildTransferSummaryAsync(Guid? storeId, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken);

    // reconciliation
    Task<StockReconciliationResponse> ReconcileAsync(Guid? storeId, CancellationToken cancellationToken);
}
