using System.Security.Cryptography;
using System.Text;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Inventory.Abstractions;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Inventory;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Inventory.Services;

/// <summary>
/// Transfers between stores with scan-based receiving (Part 19, docx 5.9 and 5.10). A transfer is a draft the sending store fills by scanning; dispatch takes the
/// pieces out of the sender (they are then in transit); the receiving store scans what arrives; closing puts the received pieces into the receiver's stock and
/// raises a loss case for every piece not scanned. Nothing is assumed about missing pieces: a loss case waits for a decision (found, or charged to a bearer).
/// Not decided and not enforced: how long a transfer may stay in transit, who carries a loss, a value above which a second person must receive.
/// </summary>
public sealed class StockTransferService(
    IInventoryPostingRepository postingRepository,
    IInventoryReadRepository readRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<StockTransferService> logger)
{
    public async Task<StockTransferResponse> CreateAsync(CreateStockTransferRequest request, CancellationToken cancellationToken)
    {
        InventoryRequestValidator.EnsureStoreAccess(currentUser, request.FromStoreId);
        InventoryRequestValidator.EnsureClientTransactionId(request.ClientTransactionId);
        if (request.ToStoreId == Guid.Empty || request.ToStoreId == request.FromStoreId)
            throw new DomainException(ErrorKind.Validation, "Choose a receiving store that is different from the sending store.");
        var note = InventoryRules.CleanOptionalText(request.Note, InventoryRules.DescriptionMaximumLength, "The description");

        var transferId = await postingRepository.CreateTransferAsync(request.FromStoreId, request.ToStoreId, request.ClientTransactionId, note, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var transfer = await FindTransferAsync(transferId, cancellationToken);
        logger.LogInformation("Stock transfer {TransferNumber} ({TransferId}) started from store {FromStoreId} to store {ToStoreId} of company {CompanyId} by user {UserId}",
            transfer.TransferNumber, transfer.Id, transfer.FromStoreId, transfer.ToStoreId, currentUser.CompanyId, currentUser.UserId);
        return transfer;
    }

    /// <summary>One scan: the sending store (phase SEND) while the transfer is a draft, the receiving store (phase RECEIVE) once it has been dispatched.</summary>
    public async Task<TransferScanResultResponse> RecordScanAsync(Guid transferId, string phaseCode, RecordTransferScanRequest request, CancellationToken cancellationToken)
    {
        var barcode = InventoryRules.CleanBarcode(request.Barcode);
        InventoryRequestValidator.EnsureScanQuantity(request.Quantity, InventoryRules.MaximumPiecesPerScan);
        if (request.ScanBatchId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "The scan has no id. Reload the screen and try again.");
        var transfer = await FindTransferAsync(transferId, cancellationToken);

        var isSending = phaseCode == TransferScanPhaseCodes.Send;
        var acceptsScans = isSending ? transfer.StatusCode == StockTransferStatusCodes.Draft : StockTransferStatusCodes.CanBeReceived(transfer.StatusCode);
        var scanningStoreId = isSending ? transfer.FromStoreId : transfer.ToStoreId;
        InventoryRequestValidator.EnsureStoreAccess(currentUser, scanningStoreId);
        if (!acceptsScans)
            throw new DomainException(ErrorKind.Conflict,
                $"Transfer {transfer.TransferNumber} is {transfer.StatusCode.ToLowerInvariant().Replace('_', ' ')} and does not accept {(isSending ? "sending" : "receiving")} scans.");

        var outcome = await postingRepository.RecordTransferScanAsync(transferId, scanningStoreId, phaseCode, barcode, request.Quantity, request.ScanBatchId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var item = await readRepository.FindItemByBarcodeAsync(scanningStoreId, barcode, cancellationToken)
                   ?? throw new DomainException(ErrorKind.NotFound, $"Barcode {barcode} is not a known article size.");
        return new TransferScanResultResponse(outcome.VariantId, item.ProductName, item.SizeCode, outcome.QuantityScanned, outcome.QuantityReceived, outcome.WasAlreadyRecorded);
    }

    /// <summary>Dispatch ("send"): the pieces leave the sender's stock and are in transit until the receiver closes the transfer.</summary>
    public async Task<StockTransferResponse> DispatchAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var transfer = await FindTransferAsync(transferId, cancellationToken);
        InventoryRequestValidator.EnsureStoreAccess(currentUser, transfer.FromStoreId);
        if (transfer.StatusCode != StockTransferStatusCodes.Draft)
            throw new DomainException(ErrorKind.Conflict, $"Transfer {transfer.TransferNumber} is already {transfer.StatusCode.ToLowerInvariant().Replace('_', ' ')}.");
        if (transfer.QuantityScanned <= 0)
            throw new DomainException(ErrorKind.Validation, "Scan at least one piece before sending the transfer.");

        await postingRepository.ShipTransferAsync(transferId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Stock transfer {TransferNumber} ({TransferId}) dispatched from store {FromStoreId} of company {CompanyId} by user {UserId}",
            transfer.TransferNumber, transferId, transfer.FromStoreId, currentUser.CompanyId, currentUser.UserId);
        return await FindTransferAsync(transferId, cancellationToken);
    }

    /// <summary>Completes the receiving: scanned pieces go into the receiver's stock, pieces not scanned become loss cases.</summary>
    public async Task<StockTransferResponse> CompleteReceivingAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var transfer = await FindTransferAsync(transferId, cancellationToken);
        InventoryRequestValidator.EnsureStoreAccess(currentUser, transfer.ToStoreId);
        if (!StockTransferStatusCodes.CanBeReceived(transfer.StatusCode))
            throw new DomainException(ErrorKind.Conflict, $"Transfer {transfer.TransferNumber} is {transfer.StatusCode.ToLowerInvariant().Replace('_', ' ')} and cannot be completed now.");

        await postingRepository.CloseTransferAsync(transferId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        var closed = await FindTransferAsync(transferId, cancellationToken);
        logger.LogInformation("Stock transfer {TransferNumber} ({TransferId}) completed in store {ToStoreId} of company {CompanyId} by user {UserId}: received {Received}, missing {Missing}",
            transfer.TransferNumber, transferId, transfer.ToStoreId, currentUser.CompanyId, currentUser.UserId, closed.QuantityReceived, closed.QuantityMissing);
        return closed;
    }

    public async Task<StockTransferResponse> CancelAsync(Guid transferId, CancelStockTransferRequest request, CancellationToken cancellationToken)
    {
        var reason = InventoryRules.CleanOptionalText(request.Reason, InventoryRules.ReasonMaximumLength, "The reason");
        var transfer = await FindTransferAsync(transferId, cancellationToken);
        InventoryRequestValidator.EnsureStoreAccess(currentUser, transfer.FromStoreId);
        if (transfer.StatusCode != StockTransferStatusCodes.Draft)
            throw new DomainException(ErrorKind.Conflict, $"Only a draft transfer can be cancelled. Transfer {transfer.TransferNumber} is {transfer.StatusCode.ToLowerInvariant().Replace('_', ' ')}.");

        await postingRepository.CancelTransferAsync(transferId, reason, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Stock transfer {TransferNumber} ({TransferId}) cancelled in store {FromStoreId} of company {CompanyId} by user {UserId}",
            transfer.TransferNumber, transferId, transfer.FromStoreId, currentUser.CompanyId, currentUser.UserId);
        return await FindTransferAsync(transferId, cancellationToken);
    }

    /// <summary>Stock Transfer Multi (docx 5.10): every destination is its own transfer, all created (and optionally dispatched) in one transaction.</summary>
    public async Task<CreateMultiStockTransferResponse> CreateMultiAsync(CreateMultiStockTransferRequest request, CancellationToken cancellationToken)
    {
        InventoryRequestValidator.EnsureStoreAccess(currentUser, request.FromStoreId);
        var destinations = request.Destinations ?? [];
        if (destinations.Count < 1 || destinations.Count > InventoryRules.MaximumDestinationStoresPerMultiTransfer)
            throw new DomainException(ErrorKind.Validation, $"Choose between 1 and {InventoryRules.MaximumDestinationStoresPerMultiTransfer} receiving stores.");
        if (destinations.GroupBy(destination => destination.ToStoreId).Any(group => group.Count() > 1))
            throw new DomainException(ErrorKind.Validation, "Each receiving store can appear only once.");
        if (destinations.GroupBy(destination => destination.ClientTransactionId).Any(group => group.Count() > 1))
            throw new DomainException(ErrorKind.Validation, "Each receiving store needs its own transaction id. Reload the screen and try again.");
        var note = InventoryRules.CleanOptionalText(request.Note, InventoryRules.DescriptionMaximumLength, "The description");

        foreach (var destination in destinations)
        {
            InventoryRequestValidator.EnsureClientTransactionId(destination.ClientTransactionId);
            if (destination.ToStoreId == Guid.Empty || destination.ToStoreId == request.FromStoreId)
                throw new DomainException(ErrorKind.Validation, "Choose receiving stores that are different from the sending store.");
            var lines = destination.Lines ?? [];
            InventoryRequestValidator.EnsureLineCount(lines.Count, InventoryRules.MaximumLinesPerMultiTransferDestination, "transfer");
            for (var index = 0; index < lines.Count; index++)
            {
                InventoryRules.CleanBarcode(lines[index].Barcode);
                InventoryRequestValidator.EnsureScanQuantity(lines[index].Quantity, InventoryRules.MaximumPiecesPerScan);
            }
        }

        var transferIds = new List<Guid>();
        foreach (var destination in destinations)
        {
            var transferId = await postingRepository.CreateTransferAsync(request.FromStoreId, destination.ToStoreId, destination.ClientTransactionId, note, cancellationToken);
            transferIds.Add(transferId);
            // A repeated request finds the transfer it made before: when that one has left the draft state it is done and is not scanned or dispatched again.
            var existing = await FindTransferAsync(transferId, cancellationToken);
            if (existing.StatusCode != StockTransferStatusCodes.Draft) continue;
            for (var index = 0; index < destination.Lines.Count; index++)
            {
                var line = destination.Lines[index];
                await postingRepository.RecordTransferScanAsync(transferId, request.FromStoreId, TransferScanPhaseCodes.Send, InventoryRules.CleanBarcode(line.Barcode),
                    line.Quantity, DeriveScanBatchId(destination.ClientTransactionId, index), cancellationToken);
            }
            if (request.Dispatch)
                await postingRepository.ShipTransferAsync(transferId, cancellationToken);
        }
        await unitOfWork.CommitAsync(cancellationToken);

        var transfers = new List<StockTransferResponse>();
        foreach (var transferId in transferIds) transfers.Add(await FindTransferAsync(transferId, cancellationToken));
        logger.LogInformation("{TransferCount} stock transfers created from store {FromStoreId} of company {CompanyId} by user {UserId}, dispatched: {Dispatched}",
            transfers.Count, request.FromStoreId, currentUser.CompanyId, currentUser.UserId, request.Dispatch);
        return new CreateMultiStockTransferResponse(transfers);
    }

    public async Task<StockTransferResponse> GetAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var transfer = await FindTransferAsync(transferId, cancellationToken);
        if (!currentUser.CanAccessStore(transfer.FromStoreId) && !currentUser.CanAccessStore(transfer.ToStoreId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this transfer.");
        return transfer;
    }

    public async Task<PagedResult<StockTransferListItemResponse>> SearchAsync(StockTransferSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (filter.StoreId is not null) InventoryRequestValidator.EnsureStoreAccess(currentUser, filter.StoreId.Value);
        if (filter.DirectionCode is not null && !TransferDirectionCodes.IsValid(filter.DirectionCode))
            throw new DomainException(ErrorKind.Validation, "The direction must be ALL, OUTGOING or INCOMING.");
        if (filter.StatusCode is not null && !StockTransferStatusCodes.IsValid(filter.StatusCode))
            throw new DomainException(ErrorKind.Validation, "The status of the transfer is not known.");
        InventoryRequestValidator.EnsureDateRange(filter.FromDate, filter.ToDate);
        return await readRepository.SearchTransfersAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    /// <summary>The receiving screen's list: transfers on their way to (or received by) one store. Drafts are not shown, they have not left the sender.</summary>
    public async Task<PagedResult<StockTransferListItemResponse>> SearchIncomingAsync(Guid storeId, string? statusCode, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        InventoryRequestValidator.EnsureStoreAccess(currentUser, storeId);
        if (statusCode is not null && (!StockTransferStatusCodes.IsValid(statusCode) || statusCode is StockTransferStatusCodes.Draft or StockTransferStatusCodes.Cancelled))
            throw new DomainException(ErrorKind.Validation, "Choose a status of a transfer that has been sent.");
        var filter = new StockTransferSearchFilter(storeId, TransferDirectionCodes.Incoming, statusCode, null, null, null, OnlySentTransfers: true);
        return await readRepository.SearchTransfersAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    // ------------------------------------------------------------------ missing pieces

    public async Task<PagedResult<TransferLossCaseResponse>> SearchLossCasesAsync(string? statusCode, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (statusCode is not null && !TransferLossCaseStatusCodes.IsValid(statusCode))
            throw new DomainException(ErrorKind.Validation, "The status of the loss case is not known.");
        return await readRepository.SearchLossCasesAsync(statusCode, pageRequest.Normalized(), cancellationToken);
    }

    public async Task<TransferLossCaseResponse> DecideLossAsync(Guid lossCaseId, DecideTransferLossRequest request, CancellationToken cancellationToken)
    {
        if (!TransferLossDecisionCodes.IsValid(request.DecisionCode))
            throw new DomainException(ErrorKind.Validation, "Decide FOUND or CHARGE.");
        if (request.DecisionCode == TransferLossDecisionCodes.Charge && !TransferLossBearerCodes.IsValid(request.BearerCode))
            throw new DomainException(ErrorKind.Validation, "Choose who carries the loss: the company, the sending store or the receiving store.");
        var lossCase = await readRepository.FindLossCaseAsync(lossCaseId, cancellationToken)
                       ?? throw new DomainException(ErrorKind.NotFound, "The loss case was not found.");
        if (!currentUser.CanAccessStore(lossCase.FromStoreId) && !currentUser.CanAccessStore(lossCase.ToStoreId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this transfer.");
        if (lossCase.StatusCode != TransferLossCaseStatusCodes.Open)
            throw new DomainException(ErrorKind.Conflict, "This loss has already been decided.");

        var bearerCode = request.DecisionCode == TransferLossDecisionCodes.Charge ? request.BearerCode : null;
        await postingRepository.DecideLossCaseAsync(lossCaseId, request.DecisionCode, bearerCode, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Loss case {LossCaseId} of transfer {TransferNumber} decided {Decision} (bearer {Bearer}) in company {CompanyId} by user {UserId}",
            lossCaseId, lossCase.TransferNumber, request.DecisionCode, bearerCode, currentUser.CompanyId, currentUser.UserId);
        return await readRepository.FindLossCaseAsync(lossCaseId, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, "The loss case was not found.");
    }

    private async Task<StockTransferResponse> FindTransferAsync(Guid transferId, CancellationToken cancellationToken) =>
        await readRepository.FindTransferAsync(transferId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The stock transfer was not found.");

    // The same line of the same request always gets the same scan id, so a repeated multi-transfer request scans nothing twice.
    private static Guid DeriveScanBatchId(Guid clientTransactionId, int lineIndex) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"multi-transfer:{clientTransactionId}:{lineIndex}")));
}
