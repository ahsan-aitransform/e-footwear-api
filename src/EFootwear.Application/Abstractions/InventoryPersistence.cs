namespace EFootwear.Application.Abstractions;

public sealed record TransferFilter(long? StoreId, string? Status, string? Direction);

public interface ITransferRepository
{
    Task<long> NextNumberAsync(long companyId, long storeId, string series, CancellationToken ct);
    Task<long> InsertAsync(long companyId, long transferNo, long fromStore, long toStore, string? note, CancellationToken ct);
    Task<Row?> GetAsync(long id, CancellationToken ct);
    Task<List<Row>> LinesAsync(long id, CancellationToken ct);
    Task<List<Row>> EventsAsync(long id, CancellationToken ct);
    Task<Page<Row>> ListAsync(TransferFilter f, PageQuery q, CancellationToken ct);
    /// <summary>Stores one scanned piece; returns false (and stores nothing) when this scan id was already received.</summary>
    Task<bool> InsertScanAsync(long companyId, long transferId, long storeId, string phase, string barcode, Guid scanUuid, long userId, CancellationToken ct);
    Task ShipAsync(long companyId, long id, long userId, CancellationToken ct);
    Task CloseAsync(long companyId, long id, long userId, CancellationToken ct);
    Task<bool> CancelDraftAsync(long companyId, long id, long userId, CancellationToken ct);
    Task<List<Row>> LossCasesAsync(string? status, CancellationToken ct);
    Task DecideLossAsync(long companyId, long caseId, string decision, string? bearer, long userId, CancellationToken ct);
    Task<List<Row>> DiscrepanciesAsync(long id, CancellationToken ct);
}

public sealed record ClaimInsert(long CompanyId, long StoreId, long ClaimNo, DateOnly Date, long VariantId, decimal Qty, string Reason, string? Note);

public sealed record AdjustLine(long VariantId, decimal Qty, decimal? UnitCost, string? Reason);

public interface IInventoryDocsRepository
{
    Task<long> NextNumberAsync(long companyId, long storeId, string series, CancellationToken ct);
    Task<long> InsertDocumentAsync(long companyId, long storeId, string docType, long docNo, DateOnly date, string? reason, long userId, CancellationToken ct);
    Task InsertDocLineAsync(long companyId, long docId, int lineNo, long variantId, decimal? expected, decimal? counted, decimal qty, decimal unitCost, string? reason, CancellationToken ct);
    Task<Row?> DocumentAsync(long id, CancellationToken ct);
    Task<List<Row>> DocLinesAsync(long id, CancellationToken ct);
    Task<Page<Row>> DocumentsAsync(long? storeId, string? docType, PageQuery q, CancellationToken ct);
    Task<long> StoreAccountAsync(long storeId, string role, CancellationToken ct);
    Task<long> InsertJournalAsync(long companyId, DateOnly date, long storeId, string sourceType, long? sourceId, string description, long userId, CancellationToken ct);
    Task AddJournalLineAsync(long companyId, long entryId, DateOnly date, int lineNo, long storeId, long accountId, long? partyId, long debit, long credit, string? description, CancellationToken ct);
    Task<decimal> AvgCostAsync(long storeId, long variantId, CancellationToken ct);

    Task<long> InsertClaimAsync(ClaimInsert c, long userId, CancellationToken ct);
    Task<Row?> ClaimAsync(long id, CancellationToken ct);
    Task<Page<Row>> ClaimsAsync(long? storeId, string? status, PageQuery q, CancellationToken ct);
    Task ApproveClaimAsync(long companyId, long id, long userId, CancellationToken ct);
    Task RejectClaimAsync(long companyId, long id, long userId, string reason, CancellationToken ct);
    Task<Row?> VariantByBarcodeAsync(string barcode, CancellationToken ct);
}
