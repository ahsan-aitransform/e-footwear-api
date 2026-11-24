using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Inventory.Abstractions;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Inventory;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Inventory.Services;

/// <summary>
/// Stock Adjustment (Part 10, docx 5.7), the count update of the non-zero stock list (docx 5.8) and the void of an adjustment. One request is one transaction:
/// the database function numbers the document, locks and moves the stock in a fixed order and books the difference, then the service commits. An adjustment that
/// would take stock below zero is refused with the size and the quantity in stock. A posted adjustment is never edited: a void is a new document that undoes it.
/// Open business decisions, not enforced here: who may adjust and up to which value, backdating, approval above a value, which accounts take the difference.
/// </summary>
public sealed class StockAdjustmentService(
    IInventoryPostingRepository postingRepository,
    IInventoryReadRepository readRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<StockAdjustmentService> logger)
{
    public async Task<StockAdjustmentResponse> CreateAsync(CreateStockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        InventoryRequestValidator.EnsureStoreAccess(currentUser, request.StoreId);
        InventoryRequestValidator.EnsureClientTransactionId(request.ClientTransactionId);
        InventoryRequestValidator.EnsureDateIsGiven(request.AdjustmentDate, "the adjustment date");
        var lines = request.Lines ?? [];
        InventoryRequestValidator.EnsureLineCount(lines.Count, InventoryRules.MaximumLinesPerAdjustment, "adjustment");
        for (var index = 0; index < lines.Count; index++)
        {
            InventoryRequestValidator.EnsureVariantIsGiven(lines[index].VariantId, index + 1);
            InventoryRules.EnsureSignedQuantityIsValid(lines[index].Quantity, index + 1);
            InventoryRequestValidator.EnsureUnitCostIsValid(lines[index].UnitCostAmount, index + 1);
        }
        InventoryRequestValidator.EnsureNoRepeatedSize(lines.Select(line => line.VariantId));

        var description = InventoryRules.CleanOptionalText(request.Description, InventoryRules.DescriptionMaximumLength, "The description");
        var movementLines = lines.Select(line => new StockMovementLine(line.VariantId, line.Quantity, line.UnitCostAmount,
            InventoryRules.CleanOptionalText(line.ReasonText, InventoryRules.ReasonMaximumLength, "The line reason"), null, null)).ToList();

        var documentId = await postingRepository.PostAdjustmentAsync(request.StoreId, request.AdjustmentDate, request.ClientTransactionId, description, movementLines,
            StockAdjustmentKindCodes.Manual, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var adjustment = await GetPostedAsync(documentId, cancellationToken);
        logger.LogInformation("Stock adjustment {DocumentNumber} ({DocumentId}) of {LineCount} lines posted in store {StoreId} of company {CompanyId} by user {UserId}",
            adjustment.DocumentNumber, adjustment.Id, adjustment.Lines.Count, adjustment.StoreId, currentUser.CompanyId, currentUser.UserId);
        return adjustment;
    }

    /// <summary>The counted stock of the non-zero stock list: the difference to the books is posted, sizes whose count equals the books are left out.</summary>
    public async Task<StockAdjustmentResponse> CreateCountUpdateAsync(CreateStockCountUpdateRequest request, CancellationToken cancellationToken)
    {
        InventoryRequestValidator.EnsureStoreAccess(currentUser, request.StoreId);
        InventoryRequestValidator.EnsureClientTransactionId(request.ClientTransactionId);
        InventoryRequestValidator.EnsureDateIsGiven(request.UpdateDate, "the date");
        var lines = request.Lines ?? [];
        InventoryRequestValidator.EnsureLineCount(lines.Count, InventoryRules.MaximumLinesPerCountUpdate, "count update");
        for (var index = 0; index < lines.Count; index++)
        {
            InventoryRequestValidator.EnsureVariantIsGiven(lines[index].VariantId, index + 1);
            if (lines[index].CountedQuantity < 0 || lines[index].CountedQuantity > InventoryRules.MaximumQuantityPerLine
                || decimal.Round(lines[index].CountedQuantity, InventoryRules.QuantityDecimalPlaces) != lines[index].CountedQuantity)
                throw new DomainException(ErrorKind.Validation, $"Line {index + 1}: the counted stock must be zero or more, with at most {InventoryRules.QuantityDecimalPlaces} decimals.");
        }
        InventoryRequestValidator.EnsureNoRepeatedSize(lines.Select(line => line.VariantId));

        var bookQuantities = await readRepository.FindStockQuantitiesAsync(request.StoreId, lines.Select(line => line.VariantId).ToList(), cancellationToken);
        var movementLines = lines
            .Select(line => (line, book: bookQuantities[line.VariantId]))
            .Where(entry => entry.line.CountedQuantity != entry.book)
            .Select(entry => new StockMovementLine(entry.line.VariantId, entry.line.CountedQuantity - entry.book, null, "Count update", entry.book, entry.line.CountedQuantity))
            .ToList();
        if (movementLines.Count == 0)
            throw new DomainException(ErrorKind.Validation, "Nothing to update: every counted stock equals the stock in the books.");

        var description = InventoryRules.CleanOptionalText(request.Description, InventoryRules.DescriptionMaximumLength, "The description");
        var documentId = await postingRepository.PostAdjustmentAsync(request.StoreId, request.UpdateDate, request.ClientTransactionId, description, movementLines,
            StockAdjustmentKindCodes.CountUpdate, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var adjustment = await GetPostedAsync(documentId, cancellationToken);
        logger.LogInformation("Stock count update {DocumentNumber} ({DocumentId}) of {LineCount} sizes posted in store {StoreId} of company {CompanyId} by user {UserId}",
            adjustment.DocumentNumber, adjustment.Id, adjustment.Lines.Count, adjustment.StoreId, currentUser.CompanyId, currentUser.UserId);
        return adjustment;
    }

    public async Task<VoidStockAdjustmentResponse> VoidAsync(Guid adjustmentId, VoidStockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var reason = InventoryRules.CleanRequiredReason(request.Reason, "The reason for voiding the adjustment");
        InventoryRequestValidator.EnsureClientTransactionId(request.ClientTransactionId);
        var original = await readRepository.FindAdjustmentAsync(adjustmentId, cancellationToken)
                       ?? throw new DomainException(ErrorKind.NotFound, "The stock adjustment was not found.");
        InventoryRequestValidator.EnsureStoreAccess(currentUser, original.StoreId);
        if (original.KindCode == StockAdjustmentKindCodes.Reversal)
            throw new DomainException(ErrorKind.Validation, "A void cannot be voided. Post a new adjustment instead.");
        if (original.StatusCode == InventoryDocumentStatusCodes.Cancelled)
            throw new DomainException(ErrorKind.Conflict, "This adjustment is already voided.");

        var reversalId = await postingRepository.VoidAdjustmentAsync(adjustmentId, reason, request.ClientTransactionId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Stock adjustment {DocumentNumber} ({DocumentId}) voided in store {StoreId} of company {CompanyId} by user {UserId}: {Reason}",
            original.DocumentNumber, adjustmentId, original.StoreId, currentUser.CompanyId, currentUser.UserId, reason);
        return new VoidStockAdjustmentResponse(await GetPostedAsync(adjustmentId, cancellationToken), await GetPostedAsync(reversalId, cancellationToken));
    }

    public async Task<StockAdjustmentResponse> GetAsync(Guid adjustmentId, CancellationToken cancellationToken)
    {
        var adjustment = await GetPostedAsync(adjustmentId, cancellationToken);
        InventoryRequestValidator.EnsureStoreAccess(currentUser, adjustment.StoreId);
        return adjustment;
    }

    public async Task<PagedResult<StockAdjustmentListItemResponse>> SearchAsync(StockAdjustmentSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (filter.StoreId is not null) InventoryRequestValidator.EnsureStoreAccess(currentUser, filter.StoreId.Value);
        InventoryRequestValidator.EnsureDateRange(filter.FromDate, filter.ToDate);
        return await readRepository.SearchAdjustmentsAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    public async Task<PagedResult<NonZeroStockRowResponse>> ListNonZeroStockAsync(Guid storeId, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        InventoryRequestValidator.EnsureStoreAccess(currentUser, storeId);
        return await readRepository.ListNonZeroStockAsync(storeId, pageRequest.Normalized(), cancellationToken);
    }

    private async Task<StockAdjustmentResponse> GetPostedAsync(Guid documentId, CancellationToken cancellationToken) =>
        await readRepository.FindAdjustmentAsync(documentId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The stock adjustment was not found.");
}
