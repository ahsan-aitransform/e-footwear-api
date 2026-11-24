using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Inventory.Abstractions;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Inventory;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Inventory.Services;

/// <summary>
/// Stock Audit (docx 5.12 and 8.24): a count by scanning, compared with the books, then "Adjust Stock" posts the differences. The count lives in its own
/// scan list, so scanning never touches stock. "Delete" is allowed only while the audit is still being counted. Not decided and not enforced: who may post an
/// audit, a tolerance, whether stock may be sold while an audit is open.
/// </summary>
public sealed class StockAuditService(
    IInventoryPostingRepository postingRepository,
    IInventoryReadRepository readRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<StockAuditService> logger)
{
    public async Task<StockAuditResponse> CreateAsync(CreateStockAuditRequest request, CancellationToken cancellationToken)
    {
        InventoryRequestValidator.EnsureStoreAccess(currentUser, request.StoreId);
        InventoryRequestValidator.EnsureClientTransactionId(request.ClientTransactionId);
        InventoryRequestValidator.EnsureDateIsGiven(request.AuditDate, "the audit date");
        var note = InventoryRules.CleanOptionalText(request.Note, InventoryRules.DescriptionMaximumLength, "The note");

        var auditId = await postingRepository.CreateAuditAsync(request.StoreId, request.AuditDate, request.ClientTransactionId, note, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Stock audit {AuditId} started in store {StoreId} of company {CompanyId} by user {UserId}", auditId, request.StoreId, currentUser.CompanyId, currentUser.UserId);
        return await GetAsync(auditId, includeUnscannedItems: false, cancellationToken);
    }

    public async Task<StockAuditScanResultResponse> RecordScanAsync(Guid auditId, RecordStockAuditScanRequest request, CancellationToken cancellationToken)
    {
        var barcode = InventoryRules.CleanBarcode(request.Barcode);
        if (request.DeviceScanId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "The scan has no id. Reload the screen and try again.");
        if (request.Quantity == 0 || Math.Abs(request.Quantity) > InventoryRules.MaximumPiecesPerAuditScan)
            throw new DomainException(ErrorKind.Validation, $"Scan between 1 and {InventoryRules.MaximumPiecesPerAuditScan} pieces at a time (a correction is a negative number).");
        var audit = await FindAuditHeaderAsync(auditId, cancellationToken);
        if (audit.StatusCode != InventoryDocumentStatusCodes.Draft)
            throw new DomainException(ErrorKind.Conflict, $"Audit {audit.DocumentNumber} is {audit.StatusCode.ToLowerInvariant()}: only an audit that is still being counted accepts scans.");

        var outcome = await postingRepository.RecordAuditScanAsync(auditId, barcode, request.Quantity, request.DeviceScanId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var item = await readRepository.FindItemByBarcodeAsync(audit.StoreId, barcode, cancellationToken)
                   ?? throw new DomainException(ErrorKind.NotFound, $"Barcode {barcode} is not a known article size.");
        return new StockAuditScanResultResponse(outcome.VariantId, item.ProductName, item.SizeCode, outcome.CountedQuantity, outcome.WasAlreadyRecorded);
    }

    public async Task<StockAuditResponse> PostAsync(Guid auditId, PostStockAuditRequest request, CancellationToken cancellationToken)
    {
        var audit = await FindAuditHeaderAsync(auditId, cancellationToken);
        if (audit.StatusCode == InventoryDocumentStatusCodes.Cancelled)
            throw new DomainException(ErrorKind.Conflict, $"Audit {audit.DocumentNumber} was deleted and cannot be posted.");
        await postingRepository.PostAuditAsync(auditId, request.IncludeUnscannedItems, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Stock audit {DocumentNumber} ({AuditId}) posted in store {StoreId} of company {CompanyId} by user {UserId}, unscanned items included: {IncludeUnscanned}",
            audit.DocumentNumber, auditId, audit.StoreId, currentUser.CompanyId, currentUser.UserId, request.IncludeUnscannedItems);
        return await GetAsync(auditId, includeUnscannedItems: false, cancellationToken);
    }

    public async Task CancelAsync(Guid auditId, CancellationToken cancellationToken)
    {
        var audit = await FindAuditHeaderAsync(auditId, cancellationToken);
        if (audit.StatusCode != InventoryDocumentStatusCodes.Draft)
            throw new DomainException(ErrorKind.Conflict, $"Audit {audit.DocumentNumber} is {audit.StatusCode.ToLowerInvariant()}: only an audit that is still being counted can be deleted.");
        await postingRepository.CancelAuditAsync(auditId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Stock audit {DocumentNumber} ({AuditId}) deleted in store {StoreId} of company {CompanyId} by user {UserId}",
            audit.DocumentNumber, auditId, audit.StoreId, currentUser.CompanyId, currentUser.UserId);
    }

    /// <summary>The comparison of the audit. IncludeUnscannedItems adds every size with stock that was not scanned (as a count of zero) while the audit is still open.</summary>
    public async Task<StockAuditResponse> GetAsync(Guid auditId, bool includeUnscannedItems, CancellationToken cancellationToken)
    {
        var audit = await readRepository.FindAuditAsync(auditId, includeUnscannedItems, cancellationToken)
                    ?? throw new DomainException(ErrorKind.NotFound, "The stock audit was not found.");
        InventoryRequestValidator.EnsureStoreAccess(currentUser, audit.StoreId);
        return audit;
    }

    public async Task<PagedResult<StockAuditListItemResponse>> SearchAsync(StockAuditSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (filter.StoreId is not null) InventoryRequestValidator.EnsureStoreAccess(currentUser, filter.StoreId.Value);
        InventoryRequestValidator.EnsureDateRange(filter.FromDate, filter.ToDate);
        return await readRepository.SearchAuditsAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    private async Task<StockAuditResponse> FindAuditHeaderAsync(Guid auditId, CancellationToken cancellationToken) => await GetAsync(auditId, includeUnscannedItems: false, cancellationToken);
}
