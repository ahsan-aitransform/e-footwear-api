using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Inventory.Abstractions;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Inventory.Repositories;

/// <summary>
/// Reads of the Inventory module: plain SQL over the inventory tables and the catalogue. Row-level security already limits every answer to the company and
/// to the stores of the user. Every report sums its figures over the lines only (one row per line, no join that repeats a line), so a total can never be multiplied.
/// </summary>
public sealed class InventoryReadRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IInventoryReadRepository
{
    private const string HeadOfficeStoreId = "00000000-0000-0000-0000-000000000000";

    // ------------------------------------------------------------------ lookups

    public async Task<IReadOnlyList<InventoryStoreResponse>> ListStoresAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var headOfficeId = Guid.Parse(HeadOfficeStoreId);
        var rows = await databaseContext.Database.SqlQuery<StoreRow>($"""
            select s.id as "Id", s.code as "Code", s.name as "Name"
              from core.stores s
             where s.company_id = {companyId} and s.is_active and s.id <> {headOfficeId}
             order by s.name
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new InventoryStoreResponse(row.Id, row.Code, row.Name, currentUser.CanAccessStore(row.Id))).ToList();
    }

    private const string ItemSelectSql = """
        select va.id as "VariantId", pr.code as "ProductCode", pr.name as "ProductName", pr.name_ur as "ProductNameUrdu", u.name as "UnitName",
               va.size_code as "SizeCode", col.name as "ColorName", va.barcode as "Barcode", coalesce(sb.qty_on_hand, 0) as "StockQuantity",
               cast(vp.purchase_price as bigint) as "PurchasePriceAmount"
          from catalog.variants va
          join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
          join catalog.variant_prices vp on vp.company_id = va.company_id and vp.variant_id = va.id
          left join master.units u on u.company_id = pr.company_id and u.id = pr.unit_id
          left join master.colors col on col.company_id = va.company_id and col.id = va.color_id
          left join inv.stock_balances sb on sb.company_id = va.company_id and sb.store_id = {1} and sb.variant_id = va.id
         where va.company_id = {0} and not va.is_deleted and not pr.is_deleted
        """;

    public async Task<InventoryItemResponse?> FindItemByBarcodeAsync(Guid storeId, string barcode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQueryRaw<ItemRow>(ItemSelectSql + " and va.barcode = {2} limit 1", currentUser.CompanyId, storeId, barcode)
            .ToListAsync(cancellationToken);
        return rows.Select(ToItem).FirstOrDefault();
    }

    public async Task<IReadOnlyList<InventoryItemResponse>> SearchItemsAsync(Guid storeId, string searchText, int limit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQueryRaw<ItemRow>(ItemSelectSql + """
               and (position(lower({2}) in lower(pr.name)) > 0 or position(lower({2}) in lower(pr.code)) > 0 or position(lower({2}) in lower(va.barcode)) > 0
                    or position(lower({2}) in lower(coalesce(pr.name_ur, ''))) > 0)
             order by pr.name, va.size_code
             limit {3}
            """, currentUser.CompanyId, storeId, searchText, limit).ToListAsync(cancellationToken);
        return rows.Select(ToItem).ToList();
    }

    public async Task<IReadOnlyList<InventoryItemResponse>> FindItemsByBarcodesAsync(Guid storeId, IReadOnlyCollection<string> barcodes, CancellationToken cancellationToken)
    {
        if (barcodes.Count == 0) return [];
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var barcodeArray = barcodes.ToArray();
        var rows = await databaseContext.Database.SqlQueryRaw<ItemRow>(ItemSelectSql + " and va.barcode = any({2})", currentUser.CompanyId, storeId, barcodeArray)
            .ToListAsync(cancellationToken);
        return rows.Select(ToItem).ToList();
    }

    private static InventoryItemResponse ToItem(ItemRow row) => new(row.VariantId, row.ProductCode, row.ProductName, row.ProductNameUrdu, row.UnitName,
        row.SizeCode, row.ColorName, row.Barcode, row.StockQuantity, row.PurchasePriceAmount);

    public async Task<PagedResult<NonZeroStockRowResponse>> ListNonZeroStockAsync(Guid storeId, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var searchText = pageRequest.SearchText;
        var skippedCount = pageRequest.SkippedItemCount;
        var pageSize = pageRequest.PageSize;

        var totalCount = await databaseContext.Database.SqlQuery<long>($"""
            select count(*) as "Value"
              from inv.stock_balances sb
              join catalog.variants va on va.company_id = sb.company_id and va.id = sb.variant_id
              join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
             where sb.company_id = {companyId} and sb.store_id = {storeId} and sb.qty_on_hand <> 0
               and (cast({searchText} as text) is null
                    or position(lower(cast({searchText} as text)) in lower(pr.name)) > 0
                    or position(lower(cast({searchText} as text)) in lower(va.barcode)) > 0)
            """).SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<NonZeroStockRowResponse>.Empty(pageRequest);

        var rows = await databaseContext.Database.SqlQuery<NonZeroStockRow>($"""
            select va.id as "VariantId", va.barcode as "Barcode", pr.name as "ProductName", va.size_code as "SizeCode",
                   cast(vp.purchase_price as bigint) as "PurchasePriceAmount", sb.qty_on_hand as "StockQuantity"
              from inv.stock_balances sb
              join catalog.variants va on va.company_id = sb.company_id and va.id = sb.variant_id
              join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
              join catalog.variant_prices vp on vp.company_id = va.company_id and vp.variant_id = va.id
             where sb.company_id = {companyId} and sb.store_id = {storeId} and sb.qty_on_hand <> 0
               and (cast({searchText} as text) is null
                    or position(lower(cast({searchText} as text)) in lower(pr.name)) > 0
                    or position(lower(cast({searchText} as text)) in lower(va.barcode)) > 0)
             order by pr.name, va.size_code, va.id
             offset {skippedCount} limit {pageSize}
            """).ToListAsync(cancellationToken);
        var items = rows.Select(row => new NonZeroStockRowResponse(row.VariantId, row.Barcode, row.ProductName, row.SizeCode, row.PurchasePriceAmount, row.StockQuantity)).ToList();
        return new PagedResult<NonZeroStockRowResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> FindStockQuantitiesAsync(Guid storeId, IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken)
    {
        if (variantIds.Count == 0) return new Dictionary<Guid, decimal>();
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var variantIdArray = variantIds.ToArray();
        var rows = await databaseContext.Database.SqlQuery<StockQuantityRow>($"""
            select sb.variant_id as "VariantId", sb.qty_on_hand as "Quantity"
              from inv.stock_balances sb
             where sb.company_id = {companyId} and sb.store_id = {storeId} and sb.variant_id = any({variantIdArray})
            """).ToListAsync(cancellationToken);
        var quantities = variantIds.ToDictionary(id => id, _ => 0m);
        foreach (var row in rows) quantities[row.VariantId] = row.Quantity;
        return quantities;
    }

    // ------------------------------------------------------------------ adjustments

    public async Task<StockAdjustmentResponse?> FindAdjustmentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var adjustmentType = InventoryDocumentTypeCodes.Adjustment;
        var header = await databaseContext.Database.SqlQuery<AdjustmentHeaderRow>($"""
            select d.id as "Id", d.store_id as "StoreId", s.name as "StoreName", d.doc_no as "DocumentNumber", d.doc_date as "DocumentDate",
                   d.status as "StatusCode", coalesce(e.kind, 'MANUAL') as "KindCode", d.reason as "Description", e.reverses_document_id as "ReversesDocumentId",
                   creator.full_name as "CreatedByName", d.posted_at as "PostedAt",
                   coalesce(t.added, 0) as "AddedQuantity", coalesce(t.removed, 0) as "RemovedQuantity", cast(coalesce(t.net_value, 0) as bigint) as "NetValueAmount",
                   cast(coalesce(t.line_count, 0) as integer) as "LineCount"
              from inv.documents d
              join core.stores s on s.company_id = d.company_id and s.id = d.store_id
              left join inv.document_extras e on e.company_id = d.company_id and e.document_id = d.id
              left join sec.users creator on creator.company_id = d.company_id and creator.id = d.created_by
              left join lateral (select sum(l.qty) filter (where l.qty > 0) as added, -sum(l.qty) filter (where l.qty < 0) as removed,
                                        round(sum(l.qty * l.unit_cost)) as net_value, count(*) as line_count
                                   from inv.document_lines l where l.company_id = d.company_id and l.document_id = d.id) t on true
             where d.company_id = {companyId} and d.id = {documentId} and d.doc_type = {adjustmentType} and not d.is_deleted
            """).SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;

        var lines = await databaseContext.Database.SqlQuery<AdjustmentLineRow>($"""
            select l.line_no as "LineNumber", l.variant_id as "VariantId", va.barcode as "Barcode", pr.name as "ProductName", u.name as "UnitName",
                   va.size_code as "SizeCode", l.expected_qty as "ExpectedQuantity", l.counted_qty as "CountedQuantity", l.qty as "Quantity",
                   cast(round(l.unit_cost) as bigint) as "UnitCostAmount", l.reason as "ReasonText"
              from inv.document_lines l
              join catalog.variants va on va.company_id = l.company_id and va.id = l.variant_id
              join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
              left join master.units u on u.company_id = pr.company_id and u.id = pr.unit_id
             where l.company_id = {companyId} and l.document_id = {documentId}
             order by l.line_no
            """).ToListAsync(cancellationToken);

        return new StockAdjustmentResponse(header.Id, header.StoreId, header.StoreName, header.DocumentNumber, header.DocumentDate, header.StatusCode, header.KindCode,
            header.Description, header.ReversesDocumentId, header.CreatedByName, header.PostedAt, header.AddedQuantity, header.RemovedQuantity, header.NetValueAmount,
            lines.Select(line => new StockAdjustmentLineResponse(line.LineNumber, line.VariantId, line.Barcode, line.ProductName, line.UnitName, line.SizeCode,
                line.ExpectedQuantity, line.CountedQuantity, line.Quantity, line.UnitCostAmount, line.ReasonText)).ToList());
    }

    public async Task<PagedResult<StockAdjustmentListItemResponse>> SearchAdjustmentsAsync(StockAdjustmentSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var adjustmentType = InventoryDocumentTypeCodes.Adjustment;
        var searchText = pageRequest.SearchText;
        var isSearchTextNumber = long.TryParse(searchText, out var searchNumber);
        var skippedCount = pageRequest.SkippedItemCount;
        var pageSize = pageRequest.PageSize;

        var totalCount = await databaseContext.Database.SqlQuery<long>($"""
            select count(*) as "Value"
              from inv.documents d
              left join inv.document_extras e on e.company_id = d.company_id and e.document_id = d.id
             where d.company_id = {companyId} and d.doc_type = {adjustmentType} and not d.is_deleted
               and (cast({filter.StoreId} as uuid) is null or d.store_id = cast({filter.StoreId} as uuid))
               and (cast({filter.FromDate} as date) is null or d.doc_date >= cast({filter.FromDate} as date))
               and (cast({filter.ToDate} as date) is null or d.doc_date <= cast({filter.ToDate} as date))
               and (cast({filter.StatusCode} as text) is null or d.status = cast({filter.StatusCode} as text))
               and (cast({filter.KindCode} as text) is null or coalesce(e.kind, 'MANUAL') = cast({filter.KindCode} as text))
               and (cast({searchText} as text) is null or (cast({isSearchTextNumber} as boolean) and d.doc_no = {searchNumber})
                    or position(lower(cast({searchText} as text)) in lower(coalesce(d.reason, ''))) > 0)
            """).SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<StockAdjustmentListItemResponse>.Empty(pageRequest);

        var rows = await databaseContext.Database.SqlQuery<AdjustmentHeaderRow>($"""
            select d.id as "Id", d.store_id as "StoreId", s.name as "StoreName", d.doc_no as "DocumentNumber", d.doc_date as "DocumentDate",
                   d.status as "StatusCode", coalesce(e.kind, 'MANUAL') as "KindCode", d.reason as "Description", e.reverses_document_id as "ReversesDocumentId",
                   cast(null as text) as "CreatedByName", d.posted_at as "PostedAt",
                   coalesce(t.added, 0) as "AddedQuantity", coalesce(t.removed, 0) as "RemovedQuantity", cast(coalesce(t.net_value, 0) as bigint) as "NetValueAmount",
                   cast(coalesce(t.line_count, 0) as integer) as "LineCount"
              from inv.documents d
              join core.stores s on s.company_id = d.company_id and s.id = d.store_id
              left join inv.document_extras e on e.company_id = d.company_id and e.document_id = d.id
              left join lateral (select sum(l.qty) filter (where l.qty > 0) as added, -sum(l.qty) filter (where l.qty < 0) as removed,
                                        round(sum(l.qty * l.unit_cost)) as net_value, count(*) as line_count
                                   from inv.document_lines l where l.company_id = d.company_id and l.document_id = d.id) t on true
             where d.company_id = {companyId} and d.doc_type = {adjustmentType} and not d.is_deleted
               and (cast({filter.StoreId} as uuid) is null or d.store_id = cast({filter.StoreId} as uuid))
               and (cast({filter.FromDate} as date) is null or d.doc_date >= cast({filter.FromDate} as date))
               and (cast({filter.ToDate} as date) is null or d.doc_date <= cast({filter.ToDate} as date))
               and (cast({filter.StatusCode} as text) is null or d.status = cast({filter.StatusCode} as text))
               and (cast({filter.KindCode} as text) is null or coalesce(e.kind, 'MANUAL') = cast({filter.KindCode} as text))
               and (cast({searchText} as text) is null or (cast({isSearchTextNumber} as boolean) and d.doc_no = {searchNumber})
                    or position(lower(cast({searchText} as text)) in lower(coalesce(d.reason, ''))) > 0)
             order by d.doc_date desc, d.doc_no desc, d.id
             offset {skippedCount} limit {pageSize}
            """).ToListAsync(cancellationToken);
        var items = rows.Select(row => new StockAdjustmentListItemResponse(row.Id, row.StoreId, row.StoreName, row.DocumentNumber, row.DocumentDate, row.StatusCode,
            row.KindCode, row.Description, row.LineCount, row.AddedQuantity, row.RemovedQuantity, row.NetValueAmount)).ToList();
        return new PagedResult<StockAdjustmentListItemResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    // ------------------------------------------------------------------ audits

    public async Task<StockAuditResponse?> FindAuditAsync(Guid auditId, bool includeUnscannedItems, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var auditType = InventoryDocumentTypeCodes.Audit;
        var header = await databaseContext.Database.SqlQuery<AuditHeaderRow>($"""
            select d.id as "Id", d.store_id as "StoreId", s.name as "StoreName", d.doc_no as "DocumentNumber", d.doc_date as "DocumentDate", d.status as "StatusCode",
                   d.reason as "Note", coalesce(e.includes_unscanned_items, false) as "IncludesUnscannedItems", d.posted_at as "PostedAt"
              from inv.documents d
              join core.stores s on s.company_id = d.company_id and s.id = d.store_id
              left join inv.document_extras e on e.company_id = d.company_id and e.document_id = d.id
             where d.company_id = {companyId} and d.id = {auditId} and d.doc_type = {auditType} and not d.is_deleted
            """).SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;

        var isDraft = header.StatusCode == InventoryDocumentStatusCodes.Draft;
        var isPosted = header.StatusCode == InventoryDocumentStatusCodes.Posted;
        var includeUnscannedStock = isDraft && includeUnscannedItems;
        var storeId = header.StoreId;
        // While counting, the book quantity is the stock now. After posting it is the quantity the posting used (kept on the variance lines; a size without a
        // variance line was counted exactly).
        var rows = await databaseContext.Database.SqlQuery<AuditLineRow>($"""
            with scanned as (
                select s.variant_id, sum(s.quantity) as quantity
                  from inv.stock_audit_scans s where s.company_id = {companyId} and s.document_id = {auditId} group by s.variant_id
            ), candidates as (
                select variant_id from scanned
                union
                select l.variant_id from inv.document_lines l where l.company_id = {companyId} and l.document_id = {auditId}
                union
                select b.variant_id from inv.stock_balances b
                 where {includeUnscannedStock} and b.company_id = {companyId} and b.store_id = {storeId} and b.qty_on_hand > 0
            )
            select va.id as "VariantId", va.barcode as "Barcode", pr.name as "ProductName", cat.name as "CategoryName", br.name as "BrandName",
                   ge.name as "GenderName", se.name as "SeasonName", va.size_code as "SizeCode",
                   case when {isPosted} then coalesce(dl.expected_qty, sc.quantity, 0) else coalesce(sb.qty_on_hand, 0) end as "ExpectedQuantity",
                   coalesce(sc.quantity, 0) as "CountedQuantity",
                   cast(vp.purchase_price as bigint) as "PurchasePriceAmount", cast(vp.sale_price as bigint) as "SalePriceAmount",
                   (sc.variant_id is not null) as "WasScanned"
              from candidates c
              join catalog.variants va on va.company_id = {companyId} and va.id = c.variant_id
              join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
              join catalog.variant_prices vp on vp.company_id = va.company_id and vp.variant_id = va.id
              left join master.categories cat on cat.company_id = pr.company_id and cat.id = pr.category_id
              left join master.brands br on br.company_id = pr.company_id and br.id = pr.brand_id
              left join master.genders ge on ge.company_id = pr.company_id and ge.id = pr.gender_id
              left join master.seasons se on se.company_id = pr.company_id and se.id = pr.season_id
              left join scanned sc on sc.variant_id = c.variant_id
              left join inv.stock_balances sb on sb.company_id = {companyId} and sb.store_id = {storeId} and sb.variant_id = c.variant_id
              left join inv.document_lines dl on dl.company_id = {companyId} and dl.document_id = {auditId} and dl.variant_id = c.variant_id
             order by pr.name, va.size_code, va.id
            """).ToListAsync(cancellationToken);

        var lines = rows.Select(row =>
        {
            var difference = row.CountedQuantity - row.ExpectedQuantity;
            return new StockAuditLineResponse(row.VariantId, row.Barcode, row.ProductName, row.CategoryName, row.BrandName, row.GenderName, row.SeasonName, row.SizeCode,
                row.ExpectedQuantity, row.CountedQuantity, difference, row.PurchasePriceAmount, row.SalePriceAmount,
                (long)Math.Round(difference * row.PurchasePriceAmount, MidpointRounding.AwayFromZero), row.WasScanned);
        }).ToList();
        var scannedLines = lines.Where(line => line.WasScanned).ToList();
        var summary = new StockAuditSummaryResponse(scannedLines.Count, lines.Count(line => line.DifferenceQuantity > 0), lines.Count(line => line.DifferenceQuantity < 0),
            scannedLines.Count(line => line.DifferenceQuantity == 0), lines.Sum(line => line.DifferenceAmount));
        return new StockAuditResponse(header.Id, header.StoreId, header.StoreName, header.DocumentNumber, header.DocumentDate, header.StatusCode, header.Note,
            header.IncludesUnscannedItems || includeUnscannedStock, header.PostedAt, summary, lines);
    }

    public async Task<PagedResult<StockAuditListItemResponse>> SearchAuditsAsync(StockAuditSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var auditType = InventoryDocumentTypeCodes.Audit;
        var skippedCount = pageRequest.SkippedItemCount;
        var pageSize = pageRequest.PageSize;

        var totalCount = await databaseContext.Database.SqlQuery<long>($"""
            select count(*) as "Value"
              from inv.documents d
             where d.company_id = {companyId} and d.doc_type = {auditType} and not d.is_deleted
               and (cast({filter.StoreId} as uuid) is null or d.store_id = cast({filter.StoreId} as uuid))
               and (cast({filter.FromDate} as date) is null or d.doc_date >= cast({filter.FromDate} as date))
               and (cast({filter.ToDate} as date) is null or d.doc_date <= cast({filter.ToDate} as date))
               and (cast({filter.StatusCode} as text) is null or d.status = cast({filter.StatusCode} as text))
            """).SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<StockAuditListItemResponse>.Empty(pageRequest);

        var rows = await databaseContext.Database.SqlQuery<AuditListRow>($"""
            select d.id as "Id", d.store_id as "StoreId", s.name as "StoreName", d.doc_no as "DocumentNumber", d.doc_date as "DocumentDate", d.status as "StatusCode",
                   d.reason as "Note", cast(coalesce(t.piece_count, 0) as integer) as "ScannedPieceCount", cast(coalesce(t.item_count, 0) as integer) as "ScannedItemCount"
              from inv.documents d
              join core.stores s on s.company_id = d.company_id and s.id = d.store_id
              left join lateral (select sum(x.quantity) as piece_count, count(distinct x.variant_id) as item_count
                                   from inv.stock_audit_scans x where x.company_id = d.company_id and x.document_id = d.id) t on true
             where d.company_id = {companyId} and d.doc_type = {auditType} and not d.is_deleted
               and (cast({filter.StoreId} as uuid) is null or d.store_id = cast({filter.StoreId} as uuid))
               and (cast({filter.FromDate} as date) is null or d.doc_date >= cast({filter.FromDate} as date))
               and (cast({filter.ToDate} as date) is null or d.doc_date <= cast({filter.ToDate} as date))
               and (cast({filter.StatusCode} as text) is null or d.status = cast({filter.StatusCode} as text))
             order by d.doc_date desc, d.doc_no desc, d.id
             offset {skippedCount} limit {pageSize}
            """).ToListAsync(cancellationToken);
        var items = rows.Select(row => new StockAuditListItemResponse(row.Id, row.StoreId, row.StoreName, row.DocumentNumber, row.DocumentDate, row.StatusCode, row.Note,
            row.ScannedPieceCount, row.ScannedItemCount)).ToList();
        return new PagedResult<StockAuditListItemResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    // ------------------------------------------------------------------ claims

    private const string ClaimSelectSql = """
        select c.id as "Id", c.store_id as "StoreId", s.name as "StoreName", c.claim_no as "ClaimNumber", c.claim_date as "ClaimDate", c.variant_id as "VariantId",
               va.barcode as "Barcode", pr.name as "ProductName", va.size_code as "SizeCode", br.name as "BrandName", ge.name as "GenderName", se.name as "SeasonName",
               c.qty as "Quantity", cast(vp.purchase_price as bigint) as "PurchasePriceAmount", c.reason as "ReasonCode", c.note as "Note", c.status as "StatusCode",
               requester.full_name as "RequestedByName", c.requested_at as "RequestedAt", decider.full_name as "DecidedByName", c.decided_at as "DecidedAt",
               c.reject_reason as "RejectReason"
          from inv.claims c
          join core.stores s on s.company_id = c.company_id and s.id = c.store_id
          join catalog.variants va on va.company_id = c.company_id and va.id = c.variant_id
          join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
          join catalog.variant_prices vp on vp.company_id = va.company_id and vp.variant_id = va.id
          left join master.brands br on br.company_id = pr.company_id and br.id = pr.brand_id
          left join master.genders ge on ge.company_id = pr.company_id and ge.id = pr.gender_id
          left join master.seasons se on se.company_id = pr.company_id and se.id = pr.season_id
          left join sec.users requester on requester.company_id = c.company_id and requester.id = c.requested_by
          left join sec.users decider on decider.company_id = c.company_id and decider.id = c.decided_by
        """;

    private static ProductClaimResponse ToClaim(ClaimRow row) => new(row.Id, row.StoreId, row.StoreName, row.ClaimNumber, row.ClaimDate, row.VariantId, row.Barcode,
        row.ProductName, row.SizeCode, row.BrandName, row.GenderName, row.SeasonName, row.Quantity, row.PurchasePriceAmount, row.ReasonCode, row.Note, row.StatusCode,
        row.RequestedByName, row.RequestedAt, row.DecidedByName, row.DecidedAt, row.RejectReason);

    public async Task<ProductClaimResponse?> FindClaimAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQueryRaw<ClaimRow>(ClaimSelectSql + " where c.company_id = {0} and c.id = {1}", currentUser.CompanyId, claimId)
            .ToListAsync(cancellationToken);
        return rows.Select(ToClaim).FirstOrDefault();
    }

    private const string ClaimFilterSql = """
         where c.company_id = {0}
           and ({1}::uuid is null or c.store_id = {1}::uuid)
           and ({2}::date is null or c.claim_date >= {2}::date)
           and ({3}::date is null or c.claim_date <= {3}::date)
           and ({4}::text is null or c.status = {4}::text)
        """;

    private object[] ClaimFilterParameters(ProductClaimSearchFilter filter) =>
        [currentUser.CompanyId, (object?)filter.StoreId ?? DBNull.Value, (object?)filter.FromDate ?? DBNull.Value, (object?)filter.ToDate ?? DBNull.Value,
         (object?)filter.StatusCode ?? DBNull.Value];

    public async Task<PagedResult<ProductClaimResponse>> SearchClaimsAsync(ProductClaimSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = ClaimFilterParameters(filter);
        var totalCount = await databaseContext.Database.SqlQueryRaw<long>(
            "select count(*) as \"Value\" from inv.claims c" + ClaimFilterSql, parameters).SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<ProductClaimResponse>.Empty(pageRequest);

        var rows = await databaseContext.Database.SqlQueryRaw<ClaimRow>(
            ClaimSelectSql + ClaimFilterSql + " order by c.claim_date desc, c.claim_no desc, c.id offset {5} limit {6}",
            [.. parameters, pageRequest.SkippedItemCount, pageRequest.PageSize]).ToListAsync(cancellationToken);
        return new PagedResult<ProductClaimResponse>(rows.Select(ToClaim).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    public async Task<ProductClaimReportResponse> BuildClaimReportAsync(ProductClaimSearchFilter filter, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQueryRaw<ClaimRow>(
            ClaimSelectSql + ClaimFilterSql + " order by c.claim_date, c.claim_no, c.id limit " + InventoryRules.MaximumFileRows, ClaimFilterParameters(filter))
            .ToListAsync(cancellationToken);
        var claims = rows.Select(ToClaim).ToList();
        return new ProductClaimReportResponse(claims, claims.Sum(claim => claim.Quantity),
            (long)Math.Round(claims.Sum(claim => claim.Quantity * claim.PurchasePriceAmount), MidpointRounding.AwayFromZero));
    }

    // ------------------------------------------------------------------ transfers

    private const string TransferSelectSql = """
        select t.id as "Id", t.transfer_no as "TransferNumber", t.from_store_id as "FromStoreId", fs.name as "FromStoreName", t.to_store_id as "ToStoreId",
               ts.name as "ToStoreName", t.status as "StatusCode", t.note as "Note", creator.full_name as "CreatedByName", t.created_at as "CreatedAt",
               t.shipped_at as "ShippedAt", t.closed_at as "ClosedAt",
               coalesce(q.scanned, 0) as "QuantityScanned", coalesce(q.shipped, 0) as "QuantityShipped", coalesce(q.received, 0) as "QuantityReceived",
               coalesce(q.missing, 0) as "QuantityMissing"
          from inv.transfers t
          join core.stores fs on fs.company_id = t.company_id and fs.id = t.from_store_id
          join core.stores ts on ts.company_id = t.company_id and ts.id = t.to_store_id
          left join sec.users creator on creator.company_id = t.company_id and creator.id = t.created_by
          left join lateral (select sum(l.qty_scanned) as scanned, sum(l.qty_shipped) as shipped, sum(l.qty_received) as received, sum(l.qty_missing) as missing
                               from inv.transfer_lines l where l.company_id = t.company_id and l.transfer_id = t.id) q on true
        """;

    public async Task<StockTransferResponse?> FindTransferAsync(Guid transferId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var headers = await databaseContext.Database.SqlQueryRaw<TransferHeaderRow>(
            TransferSelectSql + " where t.company_id = {0} and t.id = {1} and not t.is_deleted", companyId, transferId).ToListAsync(cancellationToken);
        var header = headers.FirstOrDefault();
        if (header is null) return null;

        var lines = await databaseContext.Database.SqlQuery<TransferLineRow>($"""
            select l.id as "LineId", l.variant_id as "VariantId", va.barcode as "Barcode", pr.name as "ProductName", va.size_code as "SizeCode",
                   l.qty_scanned as "QuantityScanned", l.qty_shipped as "QuantityShipped", l.qty_received as "QuantityReceived", l.qty_missing as "QuantityMissing",
                   cast(round(l.unit_cost) as bigint) as "UnitCostAmount"
              from inv.transfer_lines l
              join catalog.variants va on va.company_id = l.company_id and va.id = l.variant_id
              join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
             where l.company_id = {companyId} and l.transfer_id = {transferId}
             order by pr.name, va.size_code, l.id
            """).ToListAsync(cancellationToken);

        var lossCases = await QueryLossCasesAsync(" and c.transfer_id = {1}", [companyId, transferId], cancellationToken);
        return new StockTransferResponse(header.Id, header.TransferNumber, header.FromStoreId, header.FromStoreName, header.ToStoreId, header.ToStoreName, header.StatusCode,
            header.Note, header.CreatedByName, header.CreatedAt, header.ShippedAt, header.ClosedAt, header.QuantityScanned, header.QuantityShipped, header.QuantityReceived,
            header.QuantityMissing,
            lines.Select(line => new StockTransferLineResponse(line.LineId, line.VariantId, line.Barcode, line.ProductName, line.SizeCode, line.QuantityScanned,
                line.QuantityShipped, line.QuantityReceived, line.QuantityMissing, line.UnitCostAmount)).ToList(),
            lossCases);
    }

    public async Task<PagedResult<StockTransferListItemResponse>> SearchTransfersAsync(StockTransferSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var direction = filter.DirectionCode ?? TransferDirectionCodes.All;
        const string filterSql = """
             where t.company_id = {0} and not t.is_deleted
               and ({1}::uuid is null
                    or ({2} = 'OUTGOING' and t.from_store_id = {1}::uuid)
                    or ({2} = 'INCOMING' and t.to_store_id = {1}::uuid)
                    or ({2} = 'ALL' and (t.from_store_id = {1}::uuid or t.to_store_id = {1}::uuid)))
               and ({3}::text is null or t.status = {3}::text)
               and ({4}::date is null or t.created_at::date >= {4}::date)
               and ({5}::date is null or t.created_at::date <= {5}::date)
               and ({6}::bigint is null or t.transfer_no = {6}::bigint)
               and (not {7}::boolean or t.status not in ('DRAFT', 'CANCELLED'))
            """;
        object[] parameters = [currentUser.CompanyId, (object?)filter.StoreId ?? DBNull.Value, direction, (object?)filter.StatusCode ?? DBNull.Value,
            (object?)filter.FromDate ?? DBNull.Value, (object?)filter.ToDate ?? DBNull.Value, (object?)filter.TransferNumber ?? DBNull.Value, filter.OnlySentTransfers];

        var totalCount = await databaseContext.Database.SqlQueryRaw<long>("select count(*) as \"Value\" from inv.transfers t" + filterSql, parameters)
            .SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<StockTransferListItemResponse>.Empty(pageRequest);

        var rows = await databaseContext.Database.SqlQueryRaw<TransferHeaderRow>(
            TransferSelectSql + filterSql + " order by t.created_at desc, t.transfer_no desc, t.id offset {8} limit {9}",
            [.. parameters, pageRequest.SkippedItemCount, pageRequest.PageSize]).ToListAsync(cancellationToken);
        var items = rows.Select(row => new StockTransferListItemResponse(row.Id, row.TransferNumber, row.FromStoreId, row.FromStoreName, row.ToStoreId, row.ToStoreName,
            row.StatusCode, row.CreatedAt, row.ShippedAt, row.ClosedAt, row.QuantityScanned, row.QuantityShipped, row.QuantityReceived, row.QuantityMissing)).ToList();
        return new PagedResult<StockTransferListItemResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    private const string LossCaseSelectSql = """
        select c.id as "Id", t.id as "TransferId", t.transfer_no as "TransferNumber", t.from_store_id as "FromStoreId", fs.name as "FromStoreName",
               t.to_store_id as "ToStoreId", ts.name as "ToStoreName", l.variant_id as "VariantId", va.barcode as "Barcode", pr.name as "ProductName",
               va.size_code as "SizeCode", c.qty as "Quantity", c.reason as "Reason", c.bearer as "BearerCode", c.status as "StatusCode", c.raised_at as "RaisedAt"
          from inv.transfer_loss_cases c
          join inv.transfer_lines l on l.company_id = c.company_id and l.id = c.transfer_line_id
          join inv.transfers t on t.company_id = c.company_id and t.id = c.transfer_id
          join core.stores fs on fs.company_id = t.company_id and fs.id = t.from_store_id
          join core.stores ts on ts.company_id = t.company_id and ts.id = t.to_store_id
          join catalog.variants va on va.company_id = l.company_id and va.id = l.variant_id
          join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
         where c.company_id = {0}
        """;

    private static TransferLossCaseResponse ToLossCase(LossCaseRow row) => new(row.Id, row.TransferId, row.TransferNumber, row.FromStoreId, row.FromStoreName, row.ToStoreId,
        row.ToStoreName, row.VariantId, row.Barcode, row.ProductName, row.SizeCode, row.Quantity, row.Reason, row.BearerCode, row.StatusCode, row.RaisedAt);

    private async Task<IReadOnlyList<TransferLossCaseResponse>> QueryLossCasesAsync(string extraWhereSql, object[] parameters, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQueryRaw<LossCaseRow>(LossCaseSelectSql + extraWhereSql + " order by c.raised_at, c.id", parameters)
            .ToListAsync(cancellationToken);
        return rows.Select(ToLossCase).ToList();
    }

    public async Task<TransferLossCaseResponse?> FindLossCaseAsync(Guid lossCaseId, CancellationToken cancellationToken) =>
        (await QueryLossCasesAsync(" and c.id = {1}", [currentUser.CompanyId, lossCaseId], cancellationToken)).FirstOrDefault();

    public async Task<PagedResult<TransferLossCaseResponse>> SearchLossCasesAsync(string? statusCode, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        const string filterSql = " and ({1}::text is null or c.status = {1}::text)";
        object[] parameters = [currentUser.CompanyId, (object?)statusCode ?? DBNull.Value];
        var totalCount = await databaseContext.Database.SqlQueryRaw<long>(
            "select count(*) as \"Value\" from inv.transfer_loss_cases c where c.company_id = {0}" + filterSql, parameters).SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<TransferLossCaseResponse>.Empty(pageRequest);

        var rows = await databaseContext.Database.SqlQueryRaw<LossCaseRow>(
            LossCaseSelectSql + filterSql + " order by c.raised_at desc, c.id offset {2} limit {3}",
            [.. parameters, pageRequest.SkippedItemCount, pageRequest.PageSize]).ToListAsync(cancellationToken);
        return new PagedResult<TransferLossCaseResponse>(rows.Select(ToLossCase).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    // ------------------------------------------------------------------ reports

    // A voided adjustment and its reversal cancel each other out, so both are left out: the report shows the adjustments that are in force.
    public async Task<StockAdjustmentReportResponse> BuildAdjustmentReportAsync(StockAdjustmentSearchFilter filter, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var adjustmentType = InventoryDocumentTypeCodes.Adjustment;
        var postedStatus = InventoryDocumentStatusCodes.Posted;
        var reversalKind = StockAdjustmentKindCodes.Reversal;
        var maximumRows = InventoryRules.MaximumFileRows;
        var rows = await databaseContext.Database.SqlQuery<AdjustmentReportRow>($"""
            select s.name as "StoreName", d.doc_no as "DocumentNumber", d.doc_date as "DocumentDate", coalesce(e.kind, 'MANUAL') as "KindCode",
                   va.barcode as "Barcode", pr.name as "ProductName", va.size_code as "SizeCode", l.qty as "Quantity", cast(round(l.unit_cost) as bigint) as "UnitCostAmount"
              from inv.documents d
              join core.stores s on s.company_id = d.company_id and s.id = d.store_id
              left join inv.document_extras e on e.company_id = d.company_id and e.document_id = d.id
              join inv.document_lines l on l.company_id = d.company_id and l.document_id = d.id
              join catalog.variants va on va.company_id = l.company_id and va.id = l.variant_id
              join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
             where d.company_id = {companyId} and d.doc_type = {adjustmentType} and d.status = {postedStatus} and not d.is_deleted
               and coalesce(e.kind, 'MANUAL') <> {reversalKind}
               and (cast({filter.StoreId} as uuid) is null or d.store_id = cast({filter.StoreId} as uuid))
               and (cast({filter.FromDate} as date) is null or d.doc_date >= cast({filter.FromDate} as date))
               and (cast({filter.ToDate} as date) is null or d.doc_date <= cast({filter.ToDate} as date))
             order by d.doc_date, d.doc_no, l.line_no
             limit {maximumRows}
            """).ToListAsync(cancellationToken);
        var reportRows = rows.Select(row => new StockAdjustmentReportRowResponse(row.StoreName, row.DocumentNumber, row.DocumentDate, row.KindCode, row.Barcode,
            row.ProductName, row.SizeCode, row.Quantity > 0 ? row.Quantity : 0, row.Quantity < 0 ? -row.Quantity : 0, row.UnitCostAmount,
            (long)Math.Round(row.Quantity * row.UnitCostAmount, MidpointRounding.AwayFromZero))).ToList();
        return new StockAdjustmentReportResponse(reportRows, reportRows.Sum(row => row.AddedQuantity), reportRows.Sum(row => row.RemovedQuantity),
            reportRows.Sum(row => row.ValueAmount));
    }

    public async Task<StockTransferReportResponse> BuildTransferReportAsync(StockTransferReportFilter filter, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var scope = filter.ScopeCode;
        var transferNumber = scope == "BY_TRANSFER" ? filter.TransferNumber : null;
        var storeId = scope == "BY_STORE" ? filter.StoreId : null;
        var maximumRows = InventoryRules.MaximumFileRows;
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<TransferReportRow>($"""
            select t.id as "TransferId", t.transfer_no as "TransferNumber", fs.name as "FromStoreName", ts.name as "ToStoreName", t.status as "StatusCode",
                   t.created_at as "CreatedAt", va.barcode as "Barcode", pr.name as "ProductName", va.size_code as "SizeCode", l.qty_shipped as "QuantityShipped",
                   l.qty_received as "QuantityReceived", l.qty_missing as "QuantityMissing", cast(round(l.unit_cost) as bigint) as "UnitCostAmount"
              from inv.transfers t
              join core.stores fs on fs.company_id = t.company_id and fs.id = t.from_store_id
              join core.stores ts on ts.company_id = t.company_id and ts.id = t.to_store_id
              join inv.transfer_lines l on l.company_id = t.company_id and l.transfer_id = t.id
              join catalog.variants va on va.company_id = l.company_id and va.id = l.variant_id
              join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
             where t.company_id = {companyId} and not t.is_deleted and t.status <> 'CANCELLED'
               and (cast({transferNumber} as bigint) is null or t.transfer_no = cast({transferNumber} as bigint))
               and (cast({storeId} as uuid) is null or t.from_store_id = cast({storeId} as uuid) or t.to_store_id = cast({storeId} as uuid))
               and (cast({filter.FromDate} as date) is null or t.created_at::date >= cast({filter.FromDate} as date))
               and (cast({filter.ToDate} as date) is null or t.created_at::date <= cast({filter.ToDate} as date))
             order by t.created_at, t.transfer_no, pr.name, va.size_code, l.id
             limit {maximumRows}
            """).ToListAsync(cancellationToken);
        var reportRows = rows.Select(row => new StockTransferReportRowResponse(row.TransferId, row.TransferNumber, row.FromStoreName, row.ToStoreName, row.StatusCode,
            row.CreatedAt, row.Barcode, row.ProductName, row.SizeCode, row.QuantityShipped, row.QuantityReceived, row.QuantityMissing,
            (long)Math.Round(row.QuantityShipped * row.UnitCostAmount, MidpointRounding.AwayFromZero))).ToList();
        return new StockTransferReportResponse(reportRows, reportRows.Sum(row => row.QuantityShipped), reportRows.Sum(row => row.QuantityReceived),
            reportRows.Sum(row => row.QuantityMissing), reportRows.Sum(row => row.ValueAmount));
    }

    // Counts the transfers with count(distinct): the figures are summed over the lines, which are the only repeated rows.
    public async Task<StockTransferSummaryResponse> BuildTransferSummaryAsync(Guid? storeId, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<TransferSummaryRow>($"""
            select fs.name as "FromStoreName", ts.name as "ToStoreName", cast(count(distinct t.id) as integer) as "TransferCount",
                   coalesce(sum(l.qty_shipped), 0) as "QuantityShipped", coalesce(sum(l.qty_received), 0) as "QuantityReceived",
                   coalesce(sum(l.qty_missing), 0) as "QuantityMissing", cast(coalesce(round(sum(l.qty_shipped * l.unit_cost)), 0) as bigint) as "ValueAmount"
              from inv.transfers t
              join core.stores fs on fs.company_id = t.company_id and fs.id = t.from_store_id
              join core.stores ts on ts.company_id = t.company_id and ts.id = t.to_store_id
              join inv.transfer_lines l on l.company_id = t.company_id and l.transfer_id = t.id
             where t.company_id = {companyId} and not t.is_deleted and t.status not in ('DRAFT', 'CANCELLED')
               and (cast({storeId} as uuid) is null or t.from_store_id = cast({storeId} as uuid) or t.to_store_id = cast({storeId} as uuid))
               and (cast({fromDate} as date) is null or t.created_at::date >= cast({fromDate} as date))
               and (cast({toDate} as date) is null or t.created_at::date <= cast({toDate} as date))
             group by fs.name, ts.name
             order by fs.name, ts.name
            """).ToListAsync(cancellationToken);
        var summaryRows = rows.Select(row => new StockTransferSummaryRowResponse(row.FromStoreName, row.ToStoreName, row.TransferCount, row.QuantityShipped,
            row.QuantityReceived, row.QuantityMissing, row.ValueAmount)).ToList();
        return new StockTransferSummaryResponse(summaryRows, summaryRows.Sum(row => row.TransferCount), summaryRows.Sum(row => row.QuantityShipped),
            summaryRows.Sum(row => row.QuantityReceived), summaryRows.Sum(row => row.QuantityMissing), summaryRows.Sum(row => row.ValueAmount));
    }

    // ------------------------------------------------------------------ reconciliation

    public async Task<StockReconciliationResponse> ReconcileAsync(Guid? storeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var maximumRows = InventoryRules.MaximumFileRows;

        var mismatches = await databaseContext.Database.SqlQuery<MismatchRow>($"""
            with ledger as (
                select l.store_id, l.variant_id, sum(l.qty) as quantity
                  from inv.stock_ledger l
                 where l.company_id = {companyId} and (cast({storeId} as uuid) is null or l.store_id = cast({storeId} as uuid))
                 group by l.store_id, l.variant_id
            ), balance as (
                select b.store_id, b.variant_id, b.qty_on_hand
                  from inv.stock_balances b
                 where b.company_id = {companyId} and (cast({storeId} as uuid) is null or b.store_id = cast({storeId} as uuid))
            )
            select coalesce(b.store_id, l.store_id) as "StoreId", s.name as "StoreName", coalesce(b.variant_id, l.variant_id) as "VariantId", va.barcode as "Barcode",
                   pr.name as "ProductName", va.size_code as "SizeCode", coalesce(b.qty_on_hand, 0) as "BalanceQuantity", coalesce(l.quantity, 0) as "LedgerQuantity"
              from balance b
              full join ledger l on l.store_id = b.store_id and l.variant_id = b.variant_id
              join core.stores s on s.company_id = {companyId} and s.id = coalesce(b.store_id, l.store_id)
              join catalog.variants va on va.company_id = {companyId} and va.id = coalesce(b.variant_id, l.variant_id)
              join catalog.products pr on pr.company_id = va.company_id and pr.id = va.product_id
             where coalesce(b.qty_on_hand, 0) <> coalesce(l.quantity, 0)
             order by s.name, pr.name, va.size_code
             limit {maximumRows}
            """).ToListAsync(cancellationToken);

        var totals = await databaseContext.Database.SqlQuery<ReconciliationTotalsRow>($"""
            select cast(count(*) as integer) as "CheckedSizeCount", cast(count(*) filter (where b.qty_on_hand < 0) as integer) as "NegativeBalanceCount",
                   cast(coalesce(round(sum(b.qty_on_hand * b.avg_cost)), 0) as bigint) as "StockValueAmount"
              from inv.stock_balances b
             where b.company_id = {companyId} and (cast({storeId} as uuid) is null or b.store_id = cast({storeId} as uuid))
            """).SingleAsync(cancellationToken);

        var inTransit = await databaseContext.Database.SqlQuery<InTransitRow>($"""
            select cast((select count(*) from inv.transfers t
                          where t.company_id = {companyId} and not t.is_deleted and t.status in ('SHIPPED', 'RECEIVING')
                            and (cast({storeId} as uuid) is null or t.from_store_id = cast({storeId} as uuid) or t.to_store_id = cast({storeId} as uuid))) as integer) as "TransferCount",
                   coalesce((select sum(l.qty_shipped - l.qty_received - l.qty_missing) from inv.transfers t
                               join inv.transfer_lines l on l.company_id = t.company_id and l.transfer_id = t.id
                              where t.company_id = {companyId} and not t.is_deleted and t.status in ('SHIPPED', 'RECEIVING')
                                and (cast({storeId} as uuid) is null or t.from_store_id = cast({storeId} as uuid) or t.to_store_id = cast({storeId} as uuid))), 0) as "PieceCount",
                   cast((select count(*) from inv.transfer_loss_cases c
                           join inv.transfers t on t.company_id = c.company_id and t.id = c.transfer_id
                          where c.company_id = {companyId} and c.status = 'OPEN'
                            and (cast({storeId} as uuid) is null or t.from_store_id = cast({storeId} as uuid) or t.to_store_id = cast({storeId} as uuid))) as integer) as "OpenLossCaseCount"
            """).SingleAsync(cancellationToken);

        var mismatchResponses = mismatches.Select(row => new StockReconciliationMismatchResponse(row.StoreId, row.StoreName, row.VariantId, row.Barcode, row.ProductName,
            row.SizeCode, row.BalanceQuantity, row.LedgerQuantity, row.BalanceQuantity - row.LedgerQuantity)).ToList();
        return new StockReconciliationResponse(DateTimeOffset.UtcNow, storeId, totals.CheckedSizeCount, totals.NegativeBalanceCount, mismatchResponses, inTransit.TransferCount,
            inTransit.PieceCount, inTransit.OpenLossCaseCount, totals.StockValueAmount, mismatchResponses.Count == 0 && totals.NegativeBalanceCount == 0);
    }
}
