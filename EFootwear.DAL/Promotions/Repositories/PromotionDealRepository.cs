using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Promotions.Abstractions;
using EFootwear.BLL.Promotions.Contracts;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using EFootwear.Domain.Promotions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EFootwear.DAL.Promotions.Repositories;

/// <summary>
/// Deals: promo.bundles and promo.bundle_items. A barcode is unique among the sizes and the deals of a company (a unique index on the deals and a trigger on both tables), so a
/// deal can never be mistaken for a size at the till. Items are replaced as one set. A deleted deal stays in the database. Only the net price of each item is stored; the sale price
/// and the discount are read from the current price of the size.
/// </summary>
public sealed class PromotionDealRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IPromotionDealRepository
{
    private const int MaximumBarcodeAttempts = 1000;
    private const string BarcodePrefix = "DL";

    private const string PhaseSql = """
        case when b.status = 'DRAFT' then 'INACTIVE'
             when b.starts_at > now() then 'SCHEDULED'
             when b.ends_at is not null and b.ends_at <= now() then 'ENDED'
             else 'RUNNING' end
        """;

    private const string DealColumns = $$"""
        b.id as "Id", b.name as "Name", b.barcode as "Barcode", b.bundle_price as "BundlePrice", b.starts_at as "StartsAt", b.ends_at as "EndsAt", b.status as "StatusCode",
        {{PhaseSql}} as "PhaseCode",
        cast((select count(*) from promo.bundle_items i where i.company_id = b.company_id and i.bundle_id = b.id) as int) as "LineCount",
        cast((select coalesce(sum(i.qty), 0) from promo.bundle_items i where i.company_id = b.company_id and i.bundle_id = b.id) as numeric) as "TotalQuantity",
        b.row_ver as "RowVersion"
        """;

    public async Task<IReadOnlyList<DealResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQueryRaw<DealRow>($$"""
            select {{DealColumns}} from promo.bundles b where b.company_id = {0} and not b.is_deleted order by b.starts_at desc, b.name, b.id
            """, currentUser.CompanyId).ToListAsync(cancellationToken);
        return rows.Select(row => ToResponse(row, [])).ToList();
    }

    public async Task<DealResponse?> FindAsync(Guid dealId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var row = await databaseContext.Database.SqlQueryRaw<DealRow>($$"""
            select {{DealColumns}} from promo.bundles b where b.company_id = {0} and b.id = {1} and not b.is_deleted
            """, companyId, dealId).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var lineRows = await databaseContext.Database.SqlQuery<LineRow>($"""
            select i.variant_id as "VariantId", v.barcode as "Barcode", p.code as "ProductCode", p.name as "ProductName", v.size_code as "SizeCode",
                   cast(i.qty as numeric) as "Quantity", coalesce(vp.sale_price, 0) as "SalePrice", i.net_unit_price as "NetUnitPrice"
              from promo.bundle_items i
              join catalog.variants v on v.company_id = i.company_id and v.id = i.variant_id
              join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
              left join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
             where i.company_id = {companyId} and i.bundle_id = {dealId}
             order by p.name, case when v.size_code ~ '^[0-9]+(\.[0-9]+)?$' then v.size_code::numeric end nulls last, v.size_code, v.id
            """).ToListAsync(cancellationToken);
        var lines = lineRows.Select(line => new DealLineResponse(line.VariantId, line.Barcode, line.ProductCode, line.ProductName, line.SizeCode, line.Quantity,
                                                                 line.SalePrice, line.NetUnitPrice, (long)line.Quantity * line.NetUnitPrice)).ToList();
        return ToResponse(row, lines);
    }

    public async Task<DealVariantLookupResponse?> FindVariantByBarcodeAsync(string barcode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var storedBarcode = await BarcodeMatching.FindStoredBarcodeAsync(databaseContext, companyId, barcode, cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<LookupRow>($"""
            select v.id as "VariantId", v.barcode as "Barcode", p.code as "ProductCode", p.name as "ProductName", v.size_code as "SizeCode",
                   coalesce(vp.sale_price, 0) as "SalePrice"
              from catalog.variants v
              join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
              left join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
             where v.company_id = {companyId} and v.barcode = {storedBarcode} and not v.is_deleted and not p.is_deleted
            """).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : new DealVariantLookupResponse(rows[0].VariantId, rows[0].Barcode, rows[0].ProductCode, rows[0].ProductName, rows[0].SizeCode, rows[0].SalePrice);
    }

    public async Task<int> CountExistingVariantsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var idArray = variantIds.ToArray();
        return (int)await databaseContext.Database.SqlQuery<long>($"""
            select count(*) as "Value" from catalog.variants v where v.company_id = {companyId} and not v.is_deleted and v.id = any({idArray})
            """).SingleAsync(cancellationToken);
    }

    public async Task<string> BuildFreeBarcodeAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var dealCount = await databaseContext.Database.SqlQuery<long>($"""
            select count(*) as "Value" from promo.bundles where company_id = {companyId}
            """).SingleAsync(cancellationToken);
        for (var attempt = 1; attempt <= MaximumBarcodeAttempts; attempt++)
        {
            var candidate = $"{BarcodePrefix}{dealCount + attempt:D6}";
            var isTaken = await databaseContext.Database.SqlQuery<bool>($"""
                select exists (select 1 from promo.bundles where company_id = {companyId} and barcode = {candidate} and not is_deleted)
                    or exists (select 1 from catalog.variants where company_id = {companyId} and barcode = {candidate} and not is_deleted) as "Value"
                """).SingleAsync(cancellationToken);
            if (!isTaken) return candidate;
        }
        throw new DomainException(ErrorKind.Conflict, "No free deal barcode could be made. Type one yourself.");
    }

    public async Task<Guid?> CreateAsync(SaveDealRequest request, string barcode, long bundlePrice, bool isActive, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var statusCode = isActive ? PromotionStatusCodes.Published : PromotionStatusCodes.Draft;
        try
        {
            // INSERT ... RETURNING cannot be wrapped in a sub-select, so the rows are read with ToListAsync (no LINQ operator is added to the SQL).
            var insertedIds = await databaseContext.Database.SqlQuery<Guid>($"""
                insert into promo.bundles (company_id, name, barcode, bundle_price, starts_at, ends_at, status, created_by)
                values ({companyId}, {request.Name}, {barcode}, {bundlePrice}, {request.StartsAt}, {request.EndsAt}, {statusCode}, {userId})
                returning id as "Value"
                """).ToListAsync(cancellationToken);
            var dealId = insertedIds.Single();
            await InsertItemsAsync(dealId, request.Lines, cancellationToken);
            return dealId;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<bool> UpdateAsync(Guid dealId, SaveDealRequest request, string barcode, long bundlePrice, long expectedRowVersion, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var statusCode = request.IsActive ? PromotionStatusCodes.Published : PromotionStatusCodes.Draft;
        try
        {
            var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
                update promo.bundles
                   set name = {request.Name}, barcode = {barcode}, bundle_price = {bundlePrice}, starts_at = {request.StartsAt}, ends_at = {request.EndsAt},
                       status = {statusCode}, updated_by = {userId}
                 where company_id = {companyId} and id = {dealId} and not is_deleted and row_ver = {expectedRowVersion}
                """, cancellationToken);
            if (affectedRows == 0) return false;
            await databaseContext.Database.ExecuteSqlAsync($"delete from promo.bundle_items where company_id = {companyId} and bundle_id = {dealId}", cancellationToken);
            await InsertItemsAsync(dealId, request.Lines, cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DomainException(ErrorKind.Conflict, $"The barcode {barcode} is already used by a product or another deal.");
        }
    }

    public async Task<bool> DeleteAsync(Guid dealId, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var affectedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update promo.bundles set is_deleted = true, deleted_at = now(), deleted_by = {userId}, status = 'CANCELLED', updated_by = {userId}
             where company_id = {companyId} and id = {dealId} and not is_deleted
            """, cancellationToken);
        return affectedRows > 0;
    }

    private async Task InsertItemsAsync(Guid dealId, IReadOnlyList<SaveDealLineRequest> lines, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var variantIds = lines.Select(line => line.VariantId).ToArray();
        var quantities = lines.Select(line => line.Quantity).ToArray();
        var netPrices = lines.Select(line => line.NetUnitPrice).ToArray();
        await databaseContext.Database.ExecuteSqlAsync($"""
            insert into promo.bundle_items (company_id, bundle_id, variant_id, qty, net_unit_price)
            select {companyId}, {dealId}, x.variant_id, x.qty, x.net_unit_price
              from unnest({variantIds}, {quantities}, {netPrices}) as x(variant_id, qty, net_unit_price)
            """, cancellationToken);
    }

    private static DealResponse ToResponse(DealRow row, IReadOnlyList<DealLineResponse> lines) => new(
        row.Id, row.Name, row.Barcode, row.BundlePrice, row.StartsAt, row.EndsAt, row.StatusCode == PromotionStatusCodes.Published, row.PhaseCode,
        row.LineCount, row.TotalQuantity, lines, row.RowVersion);

    private sealed class DealRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Barcode { get; set; } = "";
        public long BundlePrice { get; set; }
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset? EndsAt { get; set; }
        public string StatusCode { get; set; } = "";
        public string PhaseCode { get; set; } = "";
        public int LineCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public long RowVersion { get; set; }
    }

    private sealed class LineRow
    {
        public Guid VariantId { get; set; }
        public string Barcode { get; set; } = "";
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string SizeCode { get; set; } = "";
        public decimal Quantity { get; set; }
        public long SalePrice { get; set; }
        public long NetUnitPrice { get; set; }
    }

    private sealed class LookupRow
    {
        public Guid VariantId { get; set; }
        public string Barcode { get; set; } = "";
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string SizeCode { get; set; } = "";
        public long SalePrice { get; set; }
    }
}
