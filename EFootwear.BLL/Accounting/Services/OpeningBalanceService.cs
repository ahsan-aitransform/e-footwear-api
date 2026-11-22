using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Accounting.Services;

/// <summary>
/// The Opening Balance screen (docx 2.18) for one store at a time. Account lines belong to the store of the account; party
/// lines belong to the head office (parties are company-wide). Saving a line again replaces its opening balance by reversal and
/// repost inside the database; lines that did not change are skipped, so saving the same screen twice posts nothing twice.
/// </summary>
public sealed class OpeningBalanceService(
    IOpeningBalanceRepository openingBalanceRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<OpeningBalanceService> logger)
{
    public async Task<OpeningBalanceSheetResponse> GetSheetAsync(Guid storeId, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(storeId);
        var rows = await openingBalanceRepository.ListRowsAsync(storeId, cancellationToken);
        return new OpeningBalanceSheetResponse(storeId, rows, rows.Sum(row => row.DebitAmount), rows.Sum(row => row.CreditAmount));
    }

    public async Task<SaveOpeningBalancesResponse> SaveAsync(SaveOpeningBalancesRequest request, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(request.StoreId);
        if (request.Lines.Count == 0)
            throw new DomainException(ErrorKind.Validation, "There is nothing to save.");

        var availableRows = await openingBalanceRepository.ListRowsAsync(request.StoreId, cancellationToken);
        var currentRows = availableRows.ToDictionary(RowKey);
        var seenLines = new HashSet<(Guid? AccountId, Guid? PartyId)>();

        var postedCount = 0;
        var unchangedCount = 0;
        var clearedCount = 0;
        foreach (var line in request.Lines)
        {
            EnsureLineIsWellFormed(line, seenLines);
            var currentRow = currentRows.GetValueOrDefault((line.AccountId, line.PartyId))
                ?? throw new DomainException(ErrorKind.Validation,
                    "A line refers to an account or party that cannot hold an opening balance in this store.");

            if (IsUnchanged(currentRow, line, request.EntryDate))
            {
                unchangedCount++;
                continue;
            }

            await openingBalanceRepository.PostAsync(line.AccountId, line.PartyId, request.StoreId, line.DebitAmount, line.CreditAmount,
                                                     request.EntryDate, cancellationToken);
            if (IsClearing(line)) clearedCount++; else postedCount++;
        }

        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Opening balances saved for store {StoreId} in company {CompanyId} by user {UserId}: {Posted} posted, {Unchanged} unchanged, {Cleared} cleared",
            request.StoreId, currentUser.CompanyId, currentUser.UserId, postedCount, unchangedCount, clearedCount);
        return new SaveOpeningBalancesResponse(postedCount, unchangedCount, clearedCount);
    }

    private static (Guid? AccountId, Guid? PartyId) RowKey(OpeningBalanceRowResponse row) => (row.AccountId, row.PartyId);

    private static bool IsClearing(OpeningBalanceLineRequest line) => line.DebitAmount + line.CreditAmount == 0;

    private static bool IsUnchanged(OpeningBalanceRowResponse currentRow, OpeningBalanceLineRequest line, DateOnly entryDate) =>
        currentRow.DebitAmount == line.DebitAmount
        && currentRow.CreditAmount == line.CreditAmount
        && (IsClearing(line) || currentRow.EntryDate == entryDate);

    private static void EnsureLineIsWellFormed(OpeningBalanceLineRequest line, HashSet<(Guid? AccountId, Guid? PartyId)> seenLines)
    {
        if (line.AccountId.HasValue == line.PartyId.HasValue)
            throw new DomainException(ErrorKind.Validation, "Every line names either an account or a party.");
        if (line.DebitAmount < 0 || line.CreditAmount < 0 || (line.DebitAmount > 0 && line.CreditAmount > 0))
            throw new DomainException(ErrorKind.Validation, "An opening balance is either a debit or a credit amount, never both.");
        if (!seenLines.Add((line.AccountId, line.PartyId)))
            throw new DomainException(ErrorKind.Validation, "An account or party appears twice in the list.");
    }

    private void EnsureStoreAccess(Guid storeId)
    {
        // The head office (nil id) is reachable only by people who may see every store.
        if (!currentUser.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
    }
}
