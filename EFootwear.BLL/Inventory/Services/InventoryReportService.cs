using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Inventory.Abstractions;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Inventory;

namespace EFootwear.BLL.Inventory.Services;

/// <summary>The inventory reports (docx 8.20, 8.21, 8.22, 8.31) and Stock Reconciliation. Read-only; each answer is capped so a report can never grow without limit.</summary>
public sealed class InventoryReportService(IInventoryReadRepository readRepository, ICurrentUser currentUser)
{
    public async Task<StockAdjustmentReportResponse> GetAdjustmentReportAsync(StockAdjustmentSearchFilter filter, CancellationToken cancellationToken)
    {
        if (filter.StoreId is not null) InventoryRequestValidator.EnsureStoreAccess(currentUser, filter.StoreId.Value);
        InventoryRequestValidator.EnsureDateRange(filter.FromDate, filter.ToDate);
        return await readRepository.BuildAdjustmentReportAsync(filter, cancellationToken);
    }

    public async Task<StockTransferReportResponse> GetTransferReportAsync(StockTransferReportFilter filter, CancellationToken cancellationToken)
    {
        if (filter.ScopeCode is not ("ALL" or "BY_TRANSFER" or "BY_STORE"))
            throw new DomainException(ErrorKind.Validation, "Choose All, By Transfer Id or By Store.");
        if (filter.ScopeCode == "BY_TRANSFER" && filter.TransferNumber is null or <= 0)
            throw new DomainException(ErrorKind.Validation, "Enter the transfer number.");
        if (filter.ScopeCode == "BY_STORE")
        {
            if (filter.StoreId is null) throw new DomainException(ErrorKind.Validation, "Choose the store.");
            InventoryRequestValidator.EnsureStoreAccess(currentUser, filter.StoreId.Value);
        }
        InventoryRequestValidator.EnsureDateRange(filter.FromDate, filter.ToDate);
        return await readRepository.BuildTransferReportAsync(filter, cancellationToken);
    }

    public async Task<StockTransferSummaryResponse> GetTransferSummaryAsync(Guid? storeId, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken)
    {
        if (storeId is not null) InventoryRequestValidator.EnsureStoreAccess(currentUser, storeId.Value);
        InventoryRequestValidator.EnsureDateRange(fromDate, toDate);
        return await readRepository.BuildTransferSummaryAsync(storeId, fromDate, toDate, cancellationToken);
    }

    public async Task<ProductClaimReportResponse> GetClaimReportAsync(ProductClaimSearchFilter filter, CancellationToken cancellationToken)
    {
        if (filter.StoreId is not null) InventoryRequestValidator.EnsureStoreAccess(currentUser, filter.StoreId.Value);
        InventoryRequestValidator.EnsureDateRange(filter.FromDate, filter.ToDate);
        return await readRepository.BuildClaimReportAsync(filter, cancellationToken);
    }

    /// <summary>Stock Reconciliation: balance against ledger. It only reports; a mismatch is investigated, never corrected here.</summary>
    public async Task<StockReconciliationResponse> ReconcileAsync(Guid? storeId, CancellationToken cancellationToken)
    {
        if (storeId is not null) InventoryRequestValidator.EnsureStoreAccess(currentUser, storeId.Value);
        return await readRepository.ReconcileAsync(storeId, cancellationToken);
    }
}
