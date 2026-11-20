using EFootwear.Application.Abstractions;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Application.Inventory;

public sealed record TransferBody(long FromStoreId, long ToStoreId, string? Note);
public sealed record ScanBody(string? Barcode, int Qty = 1, Guid? ScanUuid = null);
public sealed record LossDecisionBody(string? Decision, string? Bearer);

/// <summary>
/// Store-to-store transfers. The sender scans every piece into a DRAFT, shipping removes the stock from the sender into "stock in transit", the
/// receiver scans what arrives, and closing brings the received pieces into the receiver's stock. Anything not received becomes a loss case that
/// an authorised person later marks FOUND or charges to the company, the sender or the receiver. All of that is the database's work
/// (inv.ship_transfer, inv.close_transfer, inv.decide_loss_case); this service adds permissions and store checks.
/// </summary>
public sealed class TransferService(IUnitOfWorkFactory uows)
{
    public async Task<long> CreateAsync(UserContext u, TransferBody b, CancellationToken ct)
    {
        u.Require(P.TransferCreate);
        u.RequireStore(b.FromStoreId);
        new Check().Require(b.FromStoreId > 0 && b.ToStoreId > 0, "toStoreId", "Choose selling stores.").Require(b.FromStoreId != b.ToStoreId, "toStoreId", "The sending and receiving store must differ.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Stores.GetAsync(u.CompanyId, b.ToStoreId, ct) is null) throw new ValidationException("toStoreId", "Receiving store not found.");
        var no = await uow.Transfers.NextNumberAsync(u.CompanyId, b.FromStoreId, "TRANSFER", ct);
        var id = await uow.Transfers.InsertAsync(u.CompanyId, no, b.FromStoreId, b.ToStoreId, b.Note, ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task<Page<Row>> ListAsync(UserContext u, TransferFilter f, PageQuery q, CancellationToken ct)
    {
        if (f.StoreId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Transfers.ListAsync(f, q, ct);
    }

    public async Task<Row> GetAsync(UserContext u, long id, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await DetailAsync(uow, id, ct);
    }

    /// <summary>The sender (phase SEND) or the receiver (phase RECEIVE) scans pieces. A repeated scanUuid is ignored (an offline retry).</summary>
    public async Task<Row> ScanAsync(UserContext u, long id, string phase, ScanBody b, CancellationToken ct)
    {
        u.Require(phase == "SEND" ? P.TransferCreate : P.TransferReceive);
        new Check().NotBlank(b.Barcode, "barcode", "Barcode").Require(b.Qty is >= 1 and <= 500, "qty", "Quantity must be between 1 and 500.")
            .Require(b.ScanUuid is null || b.Qty == 1, "scanUuid", "A scan id identifies one piece; send qty 1 with it.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var t = await uow.Transfers.GetAsync(id, ct) ?? throw new NotFoundException("Transfer", id);
        var store = phase == "SEND" ? t.Get<long>("fromStoreId") : t.Get<long>("toStoreId");
        u.RequireStore(store);
        for (var i = 0; i < b.Qty; i++)
            await uow.Transfers.InsertScanAsync(u.CompanyId, id, store, phase, b.Barcode!.Trim().ToUpperInvariant(), b.ScanUuid ?? Guid.NewGuid(), u.UserId, ct);   // a repeated scanUuid is skipped
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return await DetailAsync(read, id, ct);
    }

    public async Task ShipAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.TransferCreate);
        await using var uow = await uows.BeginAsync(u, ct);
        var t = await uow.Transfers.GetAsync(id, ct) ?? throw new NotFoundException("Transfer", id);
        u.RequireStore(t.Get<long>("fromStoreId"));
        if ((await uow.Transfers.LinesAsync(id, ct)).Count(l => l.Get<decimal>("qtyScanned") > 0) == 0) throw new BusinessRuleException("Scan at least one piece before shipping.", "empty_transfer");
        await uow.DisposeAsync();
        // The transit journal line belongs to head office (store 0), which row-level security hides from store users, so the database
        // function runs in an all-stores scope. Permission and store were checked above; inv.ship_transfer re-checks the stock.
        await using var sys = await uows.BeginSystemAsync(u.CompanyId, u.UserId, ct);
        await sys.Transfers.ShipAsync(u.CompanyId, id, u.UserId, ct);
        await sys.CommitAsync(ct);
    }

    public async Task CloseAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.TransferReceive);
        await using var uow = await uows.BeginAsync(u, ct);
        var t = await uow.Transfers.GetAsync(id, ct) ?? throw new NotFoundException("Transfer", id);
        u.RequireStore(t.Get<long>("toStoreId"));
        await uow.DisposeAsync();
        await using var sys = await uows.BeginSystemAsync(u.CompanyId, u.UserId, ct);   // head-office transit lines: see ShipAsync
        await sys.Transfers.CloseAsync(u.CompanyId, id, u.UserId, ct);                  // inv.close_transfer checks TRANSFER_RECEIVE itself
        await sys.CommitAsync(ct);
    }

    public async Task CancelAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.TransferCreate);
        await using var uow = await uows.BeginAsync(u, ct);
        var t = await uow.Transfers.GetAsync(id, ct) ?? throw new NotFoundException("Transfer", id);
        u.RequireStore(t.Get<long>("fromStoreId"));
        if (!await uow.Transfers.CancelDraftAsync(u.CompanyId, id, u.UserId, ct)) throw new BusinessRuleException("Only a draft transfer can be cancelled; once shipped it must be received.", "not_draft");
        await uow.CommitAsync(ct);
    }

    public async Task<List<Row>> LossCasesAsync(UserContext u, string? status, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Transfers.LossCasesAsync(status, ct);
    }

    public async Task DecideLossAsync(UserContext u, long caseId, LossDecisionBody b, CancellationToken ct)
    {
        u.Require(P.TransferLossDecide);
        var d = (b.Decision ?? "").ToUpperInvariant();
        new Check().Require(d is "FOUND" or "CHARGE", "decision", "Decision must be FOUND or CHARGE.")
            .Require(d != "CHARGE" || b.Bearer is "COMPANY" or "SENDER_STORE" or "RECEIVER_STORE", "bearer", "Choose who bears the loss: COMPANY, SENDER_STORE or RECEIVER_STORE.").ThrowIfAny();
        await using var sys = await uows.BeginSystemAsync(u.CompanyId, u.UserId, ct);   // the loss entry may be head office's (store 0): see ShipAsync
        await sys.Transfers.DecideLossAsync(u.CompanyId, caseId, d, d == "CHARGE" ? b.Bearer : null, u.UserId, ct);   // checks TRANSFER_LOSS_DECIDE itself
        await sys.CommitAsync(ct);
    }

    private static async Task<Row> DetailAsync(IUnitOfWork uow, long id, CancellationToken ct)
    {
        var t = await uow.Transfers.GetAsync(id, ct) ?? throw new NotFoundException("Transfer", id);
        t["lines"] = await uow.Transfers.LinesAsync(id, ct);
        t["events"] = await uow.Transfers.EventsAsync(id, ct);
        t["discrepancies"] = await uow.Transfers.DiscrepanciesAsync(id, ct);
        return t;
    }
}

public sealed record AdjustLineBody(long? VariantId, string? Barcode, decimal Qty, decimal? UnitCost, string? Reason);
public sealed record AdjustBody(long? StoreId, string? Reason, List<AdjustLineBody>? Lines);
public sealed record CountLineBody(long? VariantId, string? Barcode, decimal Counted);
public sealed record AuditBody(long? StoreId, string? Reason, List<CountLineBody>? Counts);
public sealed record ClaimBody(long? StoreId, long? VariantId, string? Barcode, decimal Qty, string? Reason, string? Note);
public sealed record RejectBody(string? Reason);

/// <summary>
/// Stock adjustments, audits (counts) and claims. An adjustment or an audit difference moves stock and books the value against the store's
/// "stock loss" account (a gain is the reverse). A claim is requested by a store and, once approved, removes the pieces at average cost and
/// books the loss (inv.approve_claim).
/// </summary>
public sealed class InventoryService(IUnitOfWorkFactory uows)
{
    public async Task<Row> AdjustAsync(UserContext u, AdjustBody b, CancellationToken ct)
    {
        u.Require(P.StockAdjust);
        var store = b.StoreId ?? u.StoreId;
        u.RequireStore(store);
        var lines = b.Lines ?? [];
        new Check().Require(lines.Count > 0, "lines", "Add at least one item.").Require(lines.All(l => l.Qty != 0), "lines", "A quantity of zero changes nothing.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var resolved = new List<AdjustLine>();
        foreach (var l in lines) resolved.Add(new AdjustLine(await VariantIdAsync(uow, l.VariantId, l.Barcode, ct), l.Qty, l.UnitCost, l.Reason));
        var id = await PostDocumentAsync(u, uow, store, "ADJUSTMENT", "ADJUSTMENT", b.Reason, resolved.Select(r => (r, (decimal?)null, (decimal?)null)).ToList(), ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return await DocAsync(read, id, ct);
    }

    /// <summary>
    /// Stock audit: the counted quantity per item is compared with the book quantity now; each difference is posted and only differences are
    /// stored (the schema stores no zero-difference line). Items not counted are untouched, so a partial count is a partial audit.
    /// </summary>
    public async Task<Row> AuditAsync(UserContext u, AuditBody b, CancellationToken ct)
    {
        u.Require(P.StockAudit);
        var store = b.StoreId ?? u.StoreId;
        u.RequireStore(store);
        var counts = b.Counts ?? [];
        new Check().Require(counts.Count > 0, "counts", "Count at least one item.").Require(counts.All(c => c.Counted >= 0), "counts", "A count cannot be negative.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var byVariant = new Dictionary<long, decimal>();
        foreach (var c in counts)
        {
            var v = await VariantIdAsync(uow, c.VariantId, c.Barcode, ct);
            byVariant[v] = byVariant.GetValueOrDefault(v) + c.Counted;   // the same item counted by several scanners adds up
        }
        var onHand = await uow.StockMoves.OnHandAsync(store, byVariant.Keys, ct);
        var diffs = byVariant.Select(k => (Variant: k.Key, Expected: onHand.GetValueOrDefault(k.Key), Counted: k.Value)).Where(x => x.Counted != x.Expected).ToList();
        if (diffs.Count == 0) return new Row { ["posted"] = false, ["message"] = "Every counted item matches the books.", ["checked"] = byVariant.Count };
        var rows = diffs.Select(d => (new AdjustLine(d.Variant, d.Counted - d.Expected, null, "Audit difference"), (decimal?)d.Expected, (decimal?)d.Counted)).ToList();
        var id = await PostDocumentAsync(u, uow, store, "AUDIT", "AUDIT", b.Reason, rows, ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        var doc = await DocAsync(read, id, ct);
        doc["posted"] = true; doc["checked"] = byVariant.Count;
        return doc;
    }

    private static async Task<long> PostDocumentAsync(UserContext u, IUnitOfWork uow, long store, string docType, string series, string? reason,
                                                      List<(AdjustLine L, decimal? Expected, decimal? Counted)> lines, CancellationToken ct)
    {
        if (store <= 0) throw new ValidationException("storeId", "Choose a selling store.");
        var company = await uow.Stores.GetCompanyAsync(u.CompanyId, ct);
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(company?.Get<string>("timezone") ?? "UTC", out var z) ? z : TimeZoneInfo.Utc;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        var no = await uow.Inventory.NextNumberAsync(u.CompanyId, store, series, ct);
        var id = await uow.Inventory.InsertDocumentAsync(u.CompanyId, store, docType, no, date, reason, u.UserId, ct);

        decimal value = 0; var n = 0;
        foreach (var (l, expected, counted) in lines.OrderBy(x => x.L.VariantId))
        {
            var cost = l.Qty > 0 ? (l.UnitCost ?? await uow.Inventory.AvgCostAsync(store, l.VariantId, ct)) : 0;
            var applied = await uow.StockMoves.PostMoveAsync(u.CompanyId, store, l.VariantId, l.Qty, docType, id, cost, date, ct);
            await uow.Inventory.InsertDocLineAsync(u.CompanyId, id, ++n, l.VariantId, expected, counted, l.Qty, applied, l.Reason, ct);
            value += l.Qty * applied;
        }

        var net = Money.Round(Math.Abs(value), company?.Get<string>("roundingMode") ?? "FLOOR");
        if (net > 0)
        {
            var stock = await uow.Inventory.StoreAccountAsync(store, "STOCK", ct);
            var loss = await uow.Inventory.StoreAccountAsync(store, "STOCK_LOSS", ct);
            var entry = await uow.Inventory.InsertJournalAsync(u.CompanyId, date, store, docType, id, $"{docType.ToLowerInvariant()} {no}", u.UserId, ct);
            // a net loss debits the loss account, a net gain credits it
            var gain = value > 0;
            await uow.Inventory.AddJournalLineAsync(u.CompanyId, entry, date, 1, store, gain ? stock : loss, null, net, 0, null, ct);
            await uow.Inventory.AddJournalLineAsync(u.CompanyId, entry, date, 2, store, gain ? loss : stock, null, 0, net, null, ct);
        }
        return id;
    }

    private static async Task<long> VariantIdAsync(IUnitOfWork uow, long? variantId, string? barcode, CancellationToken ct)
    {
        if (variantId is { } v) return v;
        if (string.IsNullOrWhiteSpace(barcode)) throw new ValidationException("lines", "Scan a barcode or choose an item.");
        var hit = await uow.Inventory.VariantByBarcodeAsync(barcode.Trim(), ct) ?? throw new NotFoundException("Barcode", barcode);
        return hit.Get<long>("variantId");
    }

    public async Task<Page<Row>> DocumentsAsync(UserContext u, long? storeId, string? docType, PageQuery q, CancellationToken ct)
    {
        u.RequireAny(P.StockAdjust, P.StockAudit, P.ReportView);
        if (storeId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Inventory.DocumentsAsync(storeId, docType, q, ct);
    }

    public async Task<Row> DocumentAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.StockAdjust, P.StockAudit, P.ReportView);
        await using var uow = await uows.BeginAsync(u, ct);
        return await DocAsync(uow, id, ct);
    }

    private static async Task<Row> DocAsync(IUnitOfWork uow, long id, CancellationToken ct)
    {
        var d = await uow.Inventory.DocumentAsync(id, ct) ?? throw new NotFoundException("Document", id);
        d["lines"] = await uow.Inventory.DocLinesAsync(id, ct);
        return d;
    }

    // ------------------------------------------------------------------------------------------ claims

    public async Task<Row> RequestClaimAsync(UserContext u, ClaimBody b, CancellationToken ct)
    {
        u.Require(P.ClaimRequest);
        var store = b.StoreId ?? u.StoreId;
        u.RequireStore(store);
        new Check().Require(b.Qty > 0, "qty", "Quantity must be above zero.").Require(b.Reason is "DAMAGED" or "DEFECTIVE" or "LOST" or "OTHER", "reason", "Reason must be DAMAGED, DEFECTIVE, LOST or OTHER.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var variant = await VariantIdAsync(uow, b.VariantId, b.Barcode, ct);
        var company = await uow.Stores.GetCompanyAsync(u.CompanyId, ct);
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(company?.Get<string>("timezone") ?? "UTC", out var z) ? z : TimeZoneInfo.Utc;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        var no = await uow.Inventory.NextNumberAsync(u.CompanyId, store, "CLAIM", ct);
        var id = await uow.Inventory.InsertClaimAsync(new ClaimInsert(u.CompanyId, store, no, date, variant, b.Qty, b.Reason!, b.Note), u.UserId, ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return (await read.Inventory.ClaimAsync(id, ct))!;
    }

    public async Task<Page<Row>> ClaimsAsync(UserContext u, long? storeId, string? status, PageQuery q, CancellationToken ct)
    {
        u.RequireAny(P.ClaimRequest, P.ClaimApprove, P.ReportView);
        if (storeId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Inventory.ClaimsAsync(storeId, status, q, ct);
    }

    public async Task<Row> ClaimAsync(UserContext u, long id, CancellationToken ct)
    {
        u.RequireAny(P.ClaimRequest, P.ClaimApprove, P.ReportView);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Inventory.ClaimAsync(id, ct) ?? throw new NotFoundException("Claim", id);
    }

    public async Task<Row> ApproveClaimAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.ClaimApprove);
        await using var uow = await uows.BeginAsync(u, ct);
        var c = await uow.Inventory.ClaimAsync(id, ct) ?? throw new NotFoundException("Claim", id);
        u.RequireStore(c.Get<long>("storeId"));
        await uow.Inventory.ApproveClaimAsync(u.CompanyId, id, u.UserId, ct);
        await uow.CommitAsync(ct);
        await using var read = await uows.BeginAsync(u, ct);
        return (await read.Inventory.ClaimAsync(id, ct))!;
    }

    public async Task RejectClaimAsync(UserContext u, long id, RejectBody b, CancellationToken ct)
    {
        u.Require(P.ClaimApprove);
        new Check().NotBlank(b.Reason, "reason", "Reason").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var c = await uow.Inventory.ClaimAsync(id, ct) ?? throw new NotFoundException("Claim", id);
        u.RequireStore(c.Get<long>("storeId"));
        await uow.Inventory.RejectClaimAsync(u.CompanyId, id, u.UserId, b.Reason!.Trim(), ct);
        await uow.CommitAsync(ct);
    }
}
