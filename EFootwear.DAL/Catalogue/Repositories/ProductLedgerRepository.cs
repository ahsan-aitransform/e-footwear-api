using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Catalogue.Repositories;

/// <summary>
/// The Product Ledger (docx 4.4) from the append-only stock ledger inv.stock_ledger. Quantities in the ledger are signed (in positive, out negative);
/// the screen shows them as In and Out. Opening and closing cover all sizes of the product in the store. The date filters let PostgreSQL skip the
/// ledger's monthly partitions outside the period.
/// </summary>
public sealed class ProductLedgerRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IProductLedgerRepository
{
    public async Task<ProductLedgerResponse?> GetLedgerAsync(Guid productId, Guid storeId, DateOnly fromDate, DateOnly toDate, bool detail, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        var product = await databaseContext.Database.SqlQuery<ProductHeaderRow>($"""
            select code as "Code", name as "Name" from catalog.products
            where company_id = {companyId} and id = {productId} and not is_deleted
            """).SingleOrDefaultAsync(cancellationToken);
        var storeName = await databaseContext.Database.SqlQuery<string>($"""
            select name as "Value" from core.stores
            where company_id = {companyId} and id = {storeId} and core.store_allowed(id)
            """).SingleOrDefaultAsync(cancellationToken);
        if (product is null || storeName is null) return null;

        var opening = await databaseContext.Database.SqlQuery<decimal>($"""
            select coalesce(sum(l.qty), 0) as "Value" from inv.stock_ledger l
            join catalog.variants v on v.company_id = l.company_id and v.id = l.variant_id
            where l.company_id = {companyId} and l.store_id = {storeId} and v.product_id = {productId} and l.txn_date < {fromDate}
            """).SingleAsync(cancellationToken);

        var summaryRows = await databaseContext.Database.SqlQuery<SummaryRow>($"""
            select l.doc_type as "DocumentType",
                   coalesce(sum(l.qty) filter (where l.qty > 0), 0) as "QuantityIn",
                   coalesce(-sum(l.qty) filter (where l.qty < 0), 0) as "QuantityOut",
                   count(*)::int as "MovementCount"
            from inv.stock_ledger l
            join catalog.variants v on v.company_id = l.company_id and v.id = l.variant_id
            where l.company_id = {companyId} and l.store_id = {storeId} and v.product_id = {productId} and l.txn_date >= {fromDate} and l.txn_date <= {toDate}
            group by l.doc_type
            order by l.doc_type
            """).ToListAsync(cancellationToken);
        var summary = summaryRows.Select(row => new ProductLedgerSummaryLineResponse(row.DocumentType, row.QuantityIn, row.QuantityOut, row.MovementCount)).ToList();
        var closing = opening + summary.Sum(line => line.QuantityIn) - summary.Sum(line => line.QuantityOut);

        IReadOnlyList<ProductLedgerLineResponse> lines = [];
        if (detail)
        {
            var lineRows = await databaseContext.Database.SqlQuery<LineRow>($"""
                select l.txn_date as "TransactionDate", l.txn_at as "TransactionAt", v.size_code as "SizeCode", v.barcode as "Barcode", l.doc_type as "DocumentType",
                       l.doc_id as "DocumentId", greatest(l.qty, 0) as "QuantityIn", greatest(-l.qty, 0) as "QuantityOut", l.qty_after as "QuantityAfter",
                       l.unit_cost as "UnitCost"
                from inv.stock_ledger l
                join catalog.variants v on v.company_id = l.company_id and v.id = l.variant_id
                where l.company_id = {companyId} and l.store_id = {storeId} and v.product_id = {productId} and l.txn_date >= {fromDate} and l.txn_date <= {toDate}
                order by l.txn_at, l.id
                """).ToListAsync(cancellationToken);
            lines = lineRows.Select(row => new ProductLedgerLineResponse(row.TransactionDate, row.TransactionAt, row.SizeCode, row.Barcode, row.DocumentType,
                                                                         row.DocumentId, row.QuantityIn, row.QuantityOut, row.QuantityAfter, row.UnitCost)).ToList();
        }

        return new ProductLedgerResponse(productId, product.Code, product.Name, storeId, storeName, fromDate, toDate, opening, closing, summary, lines);
    }

    private sealed class ProductHeaderRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private sealed class SummaryRow
    {
        public string DocumentType { get; set; } = "";
        public decimal QuantityIn { get; set; }
        public decimal QuantityOut { get; set; }
        public int MovementCount { get; set; }
    }

    private sealed class LineRow
    {
        public DateOnly TransactionDate { get; set; }
        public DateTimeOffset TransactionAt { get; set; }
        public string SizeCode { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public Guid DocumentId { get; set; }
        public decimal QuantityIn { get; set; }
        public decimal QuantityOut { get; set; }
        public decimal QuantityAfter { get; set; }
        public decimal UnitCost { get; set; }
    }
}
