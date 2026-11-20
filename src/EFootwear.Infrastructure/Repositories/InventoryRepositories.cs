using EFootwear.Application.Abstractions;
using EFootwear.Infrastructure.Persistence;

namespace EFootwear.Infrastructure.Repositories;

public sealed class TransferRepository(Db db) : ITransferRepository
{
    public Task<long> NextNumberAsync(long companyId, long storeId, string series, CancellationToken ct)
        => db.ScalarAsync<long>("select core.next_number(@c, @s, @series)", new { c = companyId, s = storeId, series }, ct);

    public Task<long> InsertAsync(long companyId, long transferNo, long fromStore, long toStore, string? note, CancellationToken ct)
        => db.ScalarAsync<long>("insert into inv.transfers (company_id, transfer_no, from_store_id, to_store_id, note, created_by) values (@c, @n, @f, @t, @note, core.current_user_id()) returning id",
            new { c = companyId, n = transferNo, f = fromStore, t = toStore, note }, ct);

    public Task<Row?> GetAsync(long id, CancellationToken ct)
        => db.RowAsync(
            @"select t.id, t.transfer_no, t.from_store_id, f.name as from_store, t.to_store_id, s.name as to_store, t.status, t.note, t.created_by, t.created_at, t.shipped_by, t.shipped_at,
                     t.closed_by, t.closed_at, t.document_uuid, t.row_ver
                from inv.transfers t join core.stores f on f.company_id = t.company_id and f.id = t.from_store_id join core.stores s on s.company_id = t.company_id and s.id = t.to_store_id
               where t.id = @id and not t.is_deleted", new { id }, ct);

    public Task<List<Row>> LinesAsync(long id, CancellationToken ct)
        => db.RowsAsync(
            @"select l.id, l.variant_id, v.barcode, p.code as product_code, p.name as product_name, v.size_code, l.qty_scanned, l.qty_shipped, l.qty_received, l.qty_missing, l.unit_cost
                from inv.transfer_lines l join catalog.variants v on v.company_id = l.company_id and v.id = l.variant_id
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id where l.transfer_id = @id order by p.name, v.size_code", new { id }, ct);

    public Task<List<Row>> EventsAsync(long id, CancellationToken ct)
        => db.RowsAsync("select id, variant_id, event_type, qty, reason, user_id, occurred_at from inv.transfer_events where transfer_id = @id order by occurred_at, id", new { id }, ct);

    public Task<Page<Row>> ListAsync(TransferFilter f, PageQuery q, CancellationToken ct)
        => db.PageAsync(
            @"select t.id, t.transfer_no, t.from_store_id, f.name as from_store, t.to_store_id, s.name as to_store, t.status, t.note, t.created_at, t.shipped_at, t.closed_at,
                     (select coalesce(sum(l.qty_shipped), 0) from inv.transfer_lines l where l.company_id = t.company_id and l.transfer_id = t.id) as qty_shipped,
                     (select coalesce(sum(l.qty_received), 0) from inv.transfer_lines l where l.company_id = t.company_id and l.transfer_id = t.id) as qty_received
                from inv.transfers t join core.stores f on f.company_id = t.company_id and f.id = t.from_store_id join core.stores s on s.company_id = t.company_id and s.id = t.to_store_id
               where not t.is_deleted and (@st::text is null or t.status = @st)
                 and (@store::bigint is null or (case coalesce(@dir, 'ANY') when 'OUT' then t.from_store_id = @store when 'IN' then t.to_store_id = @store else (t.from_store_id = @store or t.to_store_id = @store) end))",
            "t.created_at desc, t.id desc", new { st = f.Status, store = f.StoreId, dir = f.Direction }, q, ct);

    public async Task<bool> InsertScanAsync(long companyId, long transferId, long storeId, string phase, string barcode, Guid scanUuid, long userId, CancellationToken ct)
    {
        // The scan gate (a BEFORE INSERT trigger) would reject a replayed scan as "over-received", so a replay is recognised first.
        if (await db.ScalarAsync<bool>("select exists (select 1 from inv.transfer_scans where company_id = @c and device_scan_uuid = @u)", new { c = companyId, u = scanUuid }, ct)) return false;
        await db.ExecAsync("insert into inv.transfer_scans (company_id, transfer_id, store_id, phase, barcode, device_scan_uuid, scanned_by) values (@c, @t, @s, @p, @b, @u, @by)",
            new { c = companyId, t = transferId, s = storeId, p = phase, b = barcode, u = scanUuid, by = userId }, ct);
        return true;
    }

    public Task ShipAsync(long companyId, long id, long userId, CancellationToken ct) => db.ExecAsync("select inv.ship_transfer(@c, @t, @u)", new { c = companyId, t = id, u = userId }, ct);
    public Task CloseAsync(long companyId, long id, long userId, CancellationToken ct) => db.ExecAsync("select inv.close_transfer(@c, @t, @u)", new { c = companyId, t = id, u = userId }, ct);

    public async Task<bool> CancelDraftAsync(long companyId, long id, long userId, CancellationToken ct)
    {
        var n = await db.ExecAsync("update inv.transfers set status = 'CANCELLED' where company_id = @c and id = @t and status = 'DRAFT'", new { c = companyId, t = id }, ct);
        if (n == 1) await db.ExecAsync("insert into inv.transfer_events (company_id, transfer_id, event_type, user_id) values (@c, @t, 'CANCEL', @u)", new { c = companyId, t = id, u = userId }, ct);
        return n == 1;
    }

    public Task<List<Row>> LossCasesAsync(string? status, CancellationToken ct)
        => db.RowsAsync(
            @"select c.id, c.transfer_id, t.transfer_no, t.from_store_id, f.name as from_store, t.to_store_id, s.name as to_store, v.barcode, p.name as product_name, v.size_code, c.qty, l.unit_cost,
                     (c.qty * l.unit_cost)::bigint as value, c.reason, c.bearer, c.status, c.raised_at, c.decided_at
                from inv.transfer_loss_cases c
                join inv.transfers t on t.company_id = c.company_id and t.id = c.transfer_id
                join core.stores f on f.company_id = t.company_id and f.id = t.from_store_id join core.stores s on s.company_id = t.company_id and s.id = t.to_store_id
                join inv.transfer_lines l on l.company_id = c.company_id and l.id = c.transfer_line_id
                join catalog.variants v on v.company_id = l.company_id and v.id = l.variant_id join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
               where (@st::text is null or c.status = @st) order by c.raised_at desc, c.id desc", new { st = status }, ct);

    public Task DecideLossAsync(long companyId, long caseId, string decision, string? bearer, long userId, CancellationToken ct)
        => db.ExecAsync("select inv.decide_loss_case(@c, @case, @d, @b, @u)", new { c = companyId, @case = caseId, d = decision, b = bearer, u = userId }, ct);

    /// <summary>Per line: what was packed, shipped, received and is missing — the shortage report of a transfer.</summary>
    public Task<List<Row>> DiscrepanciesAsync(long id, CancellationToken ct)
        => db.RowsAsync(
            @"select l.id, v.barcode, p.name as product_name, v.size_code, l.qty_shipped, l.qty_received, l.qty_shipped - l.qty_received as difference, l.qty_missing
                from inv.transfer_lines l join catalog.variants v on v.company_id = l.company_id and v.id = l.variant_id join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
               where l.transfer_id = @id and (l.qty_shipped <> l.qty_received or l.qty_missing > 0) order by p.name, v.size_code", new { id }, ct);
}

public sealed class InventoryDocsRepository(Db db) : IInventoryDocsRepository
{
    public Task<long> NextNumberAsync(long companyId, long storeId, string series, CancellationToken ct)
        => db.ScalarAsync<long>("select core.next_number(@c, @s, @series)", new { c = companyId, s = storeId, series }, ct);

    public Task<long> InsertDocumentAsync(long companyId, long storeId, string docType, long docNo, DateOnly date, string? reason, long userId, CancellationToken ct)
        => db.ScalarAsync<long>("insert into inv.documents (company_id, store_id, doc_type, doc_no, doc_date, status, reason, posted_at, posted_by, created_by) values (@c, @s, @t, @n, @d, 'POSTED', @r, now(), @u, @u) returning id",
            new { c = companyId, s = storeId, t = docType, n = docNo, d = date, r = reason, u = userId }, ct);

    public Task InsertDocLineAsync(long companyId, long docId, int lineNo, long variantId, decimal? expected, decimal? counted, decimal qty, decimal unitCost, string? reason, CancellationToken ct)
        => db.ExecAsync("insert into inv.document_lines (company_id, document_id, line_no, variant_id, expected_qty, counted_qty, qty, unit_cost, reason) values (@c, @d, @n, @v, @e, @cn, @q, @uc, @r)",
            new { c = companyId, d = docId, n = lineNo, v = variantId, e = expected, cn = counted, q = qty, uc = unitCost, r = reason }, ct);

    public Task<Row?> DocumentAsync(long id, CancellationToken ct)
        => db.RowAsync("select d.id, d.store_id, s.name as store, d.doc_type, d.doc_no, d.doc_date, d.status, d.reason, d.posted_at, d.posted_by, d.created_at from inv.documents d join core.stores s on s.company_id = d.company_id and s.id = d.store_id where d.id = @id and not d.is_deleted", new { id }, ct);

    public Task<List<Row>> DocLinesAsync(long id, CancellationToken ct)
        => db.RowsAsync(
            @"select l.line_no, l.variant_id, v.barcode, p.name as product_name, v.size_code, l.expected_qty, l.counted_qty, l.qty, l.unit_cost, (l.qty * l.unit_cost)::bigint as value, l.reason
                from inv.document_lines l join catalog.variants v on v.company_id = l.company_id and v.id = l.variant_id join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
               where l.document_id = @id order by l.line_no", new { id }, ct);

    public Task<Page<Row>> DocumentsAsync(long? storeId, string? docType, PageQuery q, CancellationToken ct)
        => db.PageAsync(
            @"select d.id, d.store_id, s.name as store, d.doc_type, d.doc_no, d.doc_date, d.status, d.reason,
                     (select count(*) from inv.document_lines l where l.company_id = d.company_id and l.document_id = d.id) as line_count,
                     (select coalesce(sum(l.qty * l.unit_cost), 0)::bigint from inv.document_lines l where l.company_id = d.company_id and l.document_id = d.id) as value
                from inv.documents d join core.stores s on s.company_id = d.company_id and s.id = d.store_id
               where not d.is_deleted and (@st::bigint is null or d.store_id = @st) and (@t::text is null or d.doc_type = @t)",
            "d.doc_date desc, d.id desc", new { st = storeId, t = docType }, q, ct);

    public Task<long> StoreAccountAsync(long storeId, string role, CancellationToken ct)
        => db.ScalarAsync<long>("select id from acct.accounts where store_id = @s and system_role = @r and is_active", new { s = storeId, r = role }, ct);

    public Task<long> InsertJournalAsync(long companyId, DateOnly date, long storeId, string sourceType, long? sourceId, string description, long userId, CancellationToken ct)
        => db.ScalarAsync<long>("insert into acct.journal_entries (company_id, entry_date, store_id, source_type, source_id, description, posted_by) values (@c, @d, @s, @t, @i, @desc, @u) returning id",
            new { c = companyId, d = date, s = storeId, t = sourceType, i = sourceId, desc = description, u = userId }, ct);

    public Task AddJournalLineAsync(long companyId, long entryId, DateOnly date, int lineNo, long storeId, long accountId, long? partyId, long debit, long credit, string? description, CancellationToken ct)
        => db.ExecAsync("select acct.add_line(@c, @e, @d, @n, @s, @a, @p, @dr, @cr, @desc)", new { c = companyId, e = entryId, d = date, n = lineNo, s = storeId, a = accountId, p = partyId, dr = debit, cr = credit, desc = description }, ct);

    public async Task<decimal> AvgCostAsync(long storeId, long variantId, CancellationToken ct)
        => await db.ScalarAsync<decimal?>("select avg_cost from inv.stock_balances where store_id = @s and variant_id = @v", new { s = storeId, v = variantId }, ct)
           ?? await db.ScalarAsync<decimal?>("select purchase_price from catalog.variant_prices where variant_id = @v", new { v = variantId }, ct) ?? 0;

    public Task<long> InsertClaimAsync(ClaimInsert c, long userId, CancellationToken ct)
        => db.ScalarAsync<long>("insert into inv.claims (company_id, store_id, claim_no, claim_date, variant_id, qty, reason, note, requested_by, created_by) values (@c, @s, @n, @d, @v, @q, @r, @note, @u, @u) returning id",
            new { c = c.CompanyId, s = c.StoreId, n = c.ClaimNo, d = c.Date, v = c.VariantId, q = c.Qty, r = c.Reason, note = c.Note, u = userId }, ct);

    public Task<Row?> ClaimAsync(long id, CancellationToken ct)
        => db.RowAsync(
            @"select c.id, c.store_id, s.name as store, c.claim_no, c.claim_date, c.variant_id, v.barcode, p.name as product_name, v.size_code, c.qty, c.reason, c.note, c.status, c.requested_by,
                     c.requested_at, c.decided_by, c.decided_at, c.reject_reason, c.unit_cost, c.journal_entry_id
                from inv.claims c join core.stores s on s.company_id = c.company_id and s.id = c.store_id join catalog.variants v on v.company_id = c.company_id and v.id = c.variant_id
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id where c.id = @id", new { id }, ct);

    public Task<Page<Row>> ClaimsAsync(long? storeId, string? status, PageQuery q, CancellationToken ct)
        => db.PageAsync(
            @"select c.id, c.store_id, s.name as store, c.claim_no, c.claim_date, v.barcode, p.name as product_name, v.size_code, c.qty, c.reason, c.status, c.unit_cost
                from inv.claims c join core.stores s on s.company_id = c.company_id and s.id = c.store_id join catalog.variants v on v.company_id = c.company_id and v.id = c.variant_id
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id where (@st::bigint is null or c.store_id = @st) and (@s::text is null or c.status = @s)",
            "c.claim_date desc, c.id desc", new { st = storeId, s = status }, q, ct);

    public Task ApproveClaimAsync(long companyId, long id, long userId, CancellationToken ct) => db.ExecAsync("select inv.approve_claim(@c, @i, @u)", new { c = companyId, i = id, u = userId }, ct);
    public Task RejectClaimAsync(long companyId, long id, long userId, string reason, CancellationToken ct) => db.ExecAsync("select inv.reject_claim(@c, @i, @u, @r)", new { c = companyId, i = id, u = userId, r = reason }, ct);

    public Task<Row?> VariantByBarcodeAsync(string barcode, CancellationToken ct)
        => db.RowAsync("select v.id as variant_id, v.barcode, p.name as product_name, v.size_code from catalog.variants v join catalog.products p on p.company_id = v.company_id and p.id = v.product_id where upper(v.barcode) = upper(@b) and not v.is_deleted", new { b = barcode }, ct);
}
