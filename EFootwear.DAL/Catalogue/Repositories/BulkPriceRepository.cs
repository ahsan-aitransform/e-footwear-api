using System.Text;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Catalogue.Repositories;

/// <summary>
/// Set-based bulk price changes (docx 4.7, 4.8, 4.10) and the price reversal of 4.12. Each change is ONE statement that picks the sizes, changes their price
/// and appends the price events, so a size is never changed without its history. A store price is an upsert into catalog.store_price_overrides; the company
/// list price is an update of catalog.variant_prices. New prices come from core.round_money (the company's FLOOR or NEAREST mode) and never exceed the
/// sanity limit of whole rupees. Column and table text is fixed in code; every value is a parameter.
/// </summary>
public sealed class BulkPriceRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IBulkPriceRepository
{
    private const string HeadOfficeStoreId = "'00000000-0000-0000-0000-000000000000'::uuid";
    private const string NaturalSizeOrder = "case when v.size_code ~ '^[0-9]+(\\.[0-9]+)?$' then v.size_code::numeric end nulls last, v.size_code";

    public Task<PagedResult<PriceChangeRowResponse>> PreviewOldToNewAsync(OldToNewPriceChangeRequest request, PageRequest pageRequest, CancellationToken cancellationToken) =>
        PreviewAsync(request.StoreId, request.DepartmentId, One(request.CategoryId), One(request.BrandId), request.PriceType, pageRequest,
            (parameters, current) => $"{current} = {parameters.Add(request.OldPrice)}", (parameters, _) => parameters.Add(request.NewPrice),
            cancellationToken);

    public Task<PagedResult<PriceChangeRowResponse>> PreviewPercentageAsync(PercentagePriceChangeRequest request, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (request is { IncreasePercent: null, DecreasePercent: null })
            return PreviewAsync(request.StoreId, request.DepartmentId, request.CategoryIds, request.BrandIds, PriceTypeCodes.Sale, pageRequest,
                (_, _) => "true", (_, current) => current, cancellationToken, (request.MinimumStock, request.MaximumStock));
        var multiplier = request.IncreasePercent is { } increase ? 100m + increase : 100m - request.DecreasePercent!.Value;
        return PreviewAsync(request.StoreId, request.DepartmentId, request.CategoryIds, request.BrandIds, PriceTypeCodes.Sale, pageRequest,
            (parameters, current) => PercentageCondition(parameters, current, multiplier),
            (parameters, current) => PercentageNewPrice(parameters, current, multiplier), cancellationToken, (request.MinimumStock, request.MaximumStock));
    }

    public async Task<int> ApplyOldToNewAsync(Guid batchId, OldToNewPriceChangeRequest request, Guid changedBy, CancellationToken cancellationToken)
    {
        var parameters = new SqlParameterList();
        var current = CurrentPriceExpression(request.PriceType, request.StoreId is not null);
        var condition = $"{current} = {parameters.Add(request.OldPrice)}";
        var newPrice = parameters.Add(request.NewPrice);
        return await ApplyAsync(batchId, request.PriceType, request.StoreId, request.DepartmentId, One(request.CategoryId), One(request.BrandId), productIds: null,
                                condition, newPrice, PriceChangeReasonCodes.BulkOldToNew, parameters, changedBy, cancellationToken);
    }

    public async Task<int> ApplyPercentageAsync(Guid batchId, PercentagePriceChangeRequest request, Guid changedBy, CancellationToken cancellationToken)
    {
        var parameters = new SqlParameterList();
        var multiplier = request.IncreasePercent is { } increase ? 100m + increase : 100m - request.DecreasePercent!.Value;
        var current = CurrentPriceExpression(PriceTypeCodes.Sale, request.StoreId is not null);
        var condition = PercentageCondition(parameters, current, multiplier);
        var newPrice = PercentageNewPrice(parameters, current, multiplier);
        return await ApplyAsync(batchId, PriceTypeCodes.Sale, request.StoreId, request.DepartmentId, request.CategoryIds, request.BrandIds, productIds: null,
                                condition, newPrice, PriceChangeReasonCodes.BulkPercentage, parameters, changedBy, cancellationToken,
                                (request.MinimumStock, request.MaximumStock));
    }

    public async Task<int> ApplyNewSalePriceAsync(Guid batchId, SetNewSalePriceRequest request, Guid changedBy, CancellationToken cancellationToken)
    {
        var parameters = new SqlParameterList();
        var current = CurrentPriceExpression(PriceTypeCodes.Sale, request.StoreId is not null);
        var newPrice = parameters.Add(request.NewSalePrice);
        var condition = $"{current} <> {newPrice}";
        return await ApplyAsync(batchId, PriceTypeCodes.Sale, request.StoreId, departmentId: null, categoryIds: null, brandIds: null, request.ProductIds.ToArray(),
                                condition, newPrice, PriceChangeReasonCodes.NewSalePrice, parameters, changedBy, cancellationToken);
    }

    public async Task<(int RestoredCount, int SkippedCount)> RestorePreviousSalePricesAsync(Guid batchId, Guid storeId, IReadOnlyCollection<Guid> productIds,
                                                                                           Guid changedBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.SaveChangesAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var productArray = productIds.ToArray();

        var candidateCount = await databaseContext.Database.SqlQuery<long>($"""
            select count(*) as "Value" from catalog.variants v
            join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
            where v.company_id = {companyId} and not v.is_deleted and p.id = any({productArray})
            """).SingleAsync(cancellationToken);

        // For each size: the scope is the store price when the store has one, else the list price. The latest SALE event of that scope is the one that can
        // be undone, and only if it is the event that set the current price and it knows its old price (an initial price has none).
        var restored = await databaseContext.Database.ExecuteSqlAsync($"""
            with candidates as (
                select v.id as variant_id, (so.variant_id is not null) as has_override, coalesce(so.sale_price, vp.sale_price) as current_price
                from catalog.variants v
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
                join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
                left join catalog.store_price_overrides so on so.company_id = v.company_id and so.variant_id = v.id and so.store_id = {storeId}
                where v.company_id = {companyId} and not v.is_deleted and p.id = any({productArray})
            ), latest as (
                select distinct on (c.variant_id) c.variant_id, c.has_override, c.current_price, e.old_price, e.new_price
                from candidates c
                left join catalog.price_events e on e.company_id = {companyId} and e.variant_id = c.variant_id and e.price_type = 'SALE'
                     and e.store_id = case when c.has_override then {storeId} else '00000000-0000-0000-0000-000000000000'::uuid end
                order by c.variant_id, e.changed_at desc nulls last, e.id desc nulls last
            ), restorable as (
                select * from latest where old_price is not null and new_price = current_price and old_price <> current_price
            ), list_updates as (
                update catalog.variant_prices vp set sale_price = r.old_price, updated_at = now(), updated_by = {changedBy}
                from restorable r where not r.has_override and vp.company_id = {companyId} and vp.variant_id = r.variant_id
                returning vp.variant_id
            ), override_updates as (
                update catalog.store_price_overrides so set sale_price = r.old_price, updated_at = now(), updated_by = {changedBy}
                from restorable r where r.has_override and so.company_id = {companyId} and so.store_id = {storeId} and so.variant_id = r.variant_id
                returning so.variant_id
            )
            insert into catalog.price_events (company_id, id, variant_id, store_id, price_type, old_price, new_price, batch_id, reason, changed_by)
            select {companyId}, uuidv7(), r.variant_id, case when r.has_override then {storeId} else '00000000-0000-0000-0000-000000000000'::uuid end,
                   'SALE', r.current_price, r.old_price, {batchId}, {PriceChangeReasonCodes.PriceReversal}, {changedBy}
            from restorable r
            """, cancellationToken);
        return (restored, (int)candidateCount - restored);
    }

    private async Task<int> ApplyAsync(Guid batchId, string priceType, Guid? storeId, Guid? departmentId, IReadOnlyList<Guid>? categoryIds, IReadOnlyList<Guid>? brandIds, Guid[]? productIds,
                                       string condition, string newPriceExpression, string reason, SqlParameterList parameters, Guid changedBy,
                                       CancellationToken cancellationToken, (decimal? Minimum, decimal? Maximum) stockRange = default)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.SaveChangesAsync(cancellationToken);
        var company = parameters.Add(currentUser.CompanyId);
        var batch = parameters.Add(batchId);
        var user = parameters.Add(changedBy);
        var reasonParameter = parameters.Add(reason);
        var priceTypeParameter = parameters.Add(priceType);
        var store = storeId is { } id ? parameters.Add(id) : null;
        var current = CurrentPriceExpression(priceType, store is not null);
        var filters = BuildFilters(departmentId, categoryIds, brandIds, productIds, parameters) + BuildStockRangeFilter(stockRange, store, parameters);
        var storeJoin = store is null ? "" : $"left join catalog.store_price_overrides so on so.company_id = v.company_id and so.variant_id = v.id and so.store_id = {store}";
        var maximum = CataloguePriceRules.MaximumPrice;

        string sql;
        if (priceType == PriceTypeCodes.Purchase)
        {
            sql = $"""
                with target as (
                    select v.id as variant_id, vp.purchase_price as old_price, ({newPriceExpression}) as new_price
                    from catalog.variants v
                    join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
                    join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
                    where v.company_id = {company} and not v.is_deleted and ({condition}) and ({newPriceExpression}) between 0 and {maximum} {filters}
                    for update of vp
                ), changed as (
                    update catalog.variant_prices vp set purchase_price = t.new_price, updated_at = now(), updated_by = {user}
                    from target t where vp.company_id = {company} and vp.variant_id = t.variant_id
                    returning vp.variant_id
                )
                insert into catalog.price_events (company_id, id, variant_id, store_id, price_type, old_price, new_price, batch_id, reason, changed_by)
                select {company}, uuidv7(), t.variant_id, {HeadOfficeStoreId}, {priceTypeParameter}, t.old_price, t.new_price, {batch}, {reasonParameter}, {user}
                from target t join changed c on c.variant_id = t.variant_id
                """;
        }
        else if (store is null)
        {
            sql = $"""
                with target as (
                    select v.id as variant_id, vp.sale_price as old_price, ({newPriceExpression}) as new_price
                    from catalog.variants v
                    join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
                    join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
                    where v.company_id = {company} and not v.is_deleted and ({condition}) and ({newPriceExpression}) between 0 and {maximum} {filters}
                    for update of vp
                ), changed as (
                    update catalog.variant_prices vp set sale_price = t.new_price, updated_at = now(), updated_by = {user}
                    from target t where vp.company_id = {company} and vp.variant_id = t.variant_id
                    returning vp.variant_id
                )
                insert into catalog.price_events (company_id, id, variant_id, store_id, price_type, old_price, new_price, batch_id, reason, changed_by)
                select {company}, uuidv7(), t.variant_id, {HeadOfficeStoreId}, {priceTypeParameter}, t.old_price, t.new_price, {batch}, {reasonParameter}, {user}
                from target t join changed c on c.variant_id = t.variant_id
                """;
        }
        else
        {
            sql = $"""
                with target as (
                    select v.id as variant_id, {current} as old_price, ({newPriceExpression}) as new_price
                    from catalog.variants v
                    join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
                    join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
                    {storeJoin}
                    where v.company_id = {company} and not v.is_deleted and ({condition}) and ({newPriceExpression}) between 0 and {maximum} {filters}
                ), changed as (
                    insert into catalog.store_price_overrides (company_id, store_id, variant_id, sale_price, updated_at, updated_by)
                    select {company}, {store}, t.variant_id, t.new_price, now(), {user} from target t
                    on conflict (company_id, store_id, variant_id) do update
                        set sale_price = excluded.sale_price, updated_at = now(), updated_by = excluded.updated_by
                    returning variant_id
                )
                insert into catalog.price_events (company_id, id, variant_id, store_id, price_type, old_price, new_price, batch_id, reason, changed_by)
                select {company}, uuidv7(), t.variant_id, {store}, {priceTypeParameter}, t.old_price, t.new_price, {batch}, {reasonParameter}, {user}
                from target t join changed c on c.variant_id = t.variant_id
                """;
        }
        return await databaseContext.Database.ExecuteSqlRawAsync(sql, parameters.ToArray());
    }

    private async Task<PagedResult<PriceChangeRowResponse>> PreviewAsync(Guid? storeId, Guid? departmentId, IReadOnlyList<Guid>? categoryIds, IReadOnlyList<Guid>? brandIds, string priceType,
                                                                        PageRequest pageRequest, Func<SqlParameterList, string, string> condition,
                                                                        Func<SqlParameterList, string, string> newPrice, CancellationToken cancellationToken,
                                                                        (decimal? Minimum, decimal? Maximum) stockRange = default)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = new SqlParameterList();
        var company = parameters.Add(currentUser.CompanyId);
        var store = storeId is { } id ? parameters.Add(id) : null;
        var current = CurrentPriceExpression(priceType, store is not null);
        var conditionSql = condition(parameters, current);
        var newPriceSql = newPrice(parameters, current);
        var filters = BuildFilters(departmentId, categoryIds, brandIds, null, parameters) + BuildStockRangeFilter(stockRange, store, parameters);
        var storeJoin = store is null ? "" : $"left join catalog.store_price_overrides so on so.company_id = v.company_id and so.variant_id = v.id and so.store_id = {store}";
        var stockCondition = store is null ? "" : $"and sb.store_id = {store}";
        var currentSale = store is null ? "vp.sale_price" : "coalesce(so.sale_price, vp.sale_price)";
        var maximum = CataloguePriceRules.MaximumPrice;
        var take = parameters.Add(pageRequest.PageSize);
        var skip = parameters.Add(pageRequest.SkippedItemCount);

        var rows = await databaseContext.Database.SqlQueryRaw<PreviewRow>($"""
            select count(*) over() as "TotalCount", p.id as "ProductId", v.id as "VariantId", p.code as "ProductCode", p.name as "ProductName", v.size_code as "SizeCode",
                   v.barcode as "Barcode", c.name as "CategoryName", b.name as "BrandName", g.name as "GenderName", s.name as "SeasonName",
                   coalesce(stock.quantity, 0) as "StockQuantity", vp.purchase_price as "PurchasePrice", {currentSale} as "CurrentSalePrice",
                   {current} as "CurrentPrice", ({newPriceSql}) as "NewPrice"
            from catalog.variants v
            join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
            join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
            join master.categories c on c.company_id = p.company_id and c.id = p.category_id
            left join master.brands b on b.company_id = p.company_id and b.id = p.brand_id
            left join master.genders g on g.company_id = p.company_id and g.id = p.gender_id
            left join master.seasons s on s.company_id = p.company_id and s.id = p.season_id
            {storeJoin}
            left join lateral (select sum(sb.qty_on_hand) as quantity from inv.stock_balances sb
                               where sb.company_id = v.company_id and sb.variant_id = v.id {stockCondition}) stock on true
            where v.company_id = {company} and not v.is_deleted and ({conditionSql}) and ({newPriceSql}) between 0 and {maximum} {filters}
            order by p.name, p.id, {NaturalSizeOrder}
            limit {take} offset {skip}
            """, parameters.ToArray()).ToListAsync(cancellationToken);

        var items = rows.Select(row => new PriceChangeRowResponse(row.ProductId, row.VariantId, row.ProductCode, row.ProductName, row.SizeCode, row.Barcode,
            row.CategoryName, row.BrandName, row.GenderName, row.SeasonName, row.StockQuantity, row.PurchasePrice, row.CurrentSalePrice, row.CurrentPrice, row.NewPrice)).ToList();
        return new PagedResult<PriceChangeRowResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    private static Guid[]? One(Guid? id) => id is { } value ? [value] : null;

    private static string CurrentPriceExpression(string priceType, bool hasStore) =>
        priceType == PriceTypeCodes.Purchase ? "vp.purchase_price" : hasStore ? "coalesce(so.sale_price, vp.sale_price)" : "vp.sale_price";

    private string PercentageNewPrice(SqlParameterList parameters, string current, decimal multiplier) =>
        $"core.round_money({parameters.Add(currentUser.CompanyId)}, {current} * {parameters.Add(multiplier)} / 100.0)";

    private string PercentageCondition(SqlParameterList parameters, string current, decimal multiplier) =>
        $"{PercentageNewPrice(parameters, current, multiplier)} <> {current}";

    /// <summary>The stock of the size in the store, or in all stores together when no store is chosen.</summary>
    private static string BuildStockRangeFilter((decimal? Minimum, decimal? Maximum) range, string? store, SqlParameterList parameters)
    {
        if (range is { Minimum: null, Maximum: null }) return "";
        var storeCondition = store is null ? "" : $" and q.store_id = {store}";
        var quantity = $"coalesce((select sum(q.qty_on_hand) from inv.stock_balances q where q.company_id = v.company_id and q.variant_id = v.id{storeCondition}), 0)";
        var minimum = range.Minimum is { } lowest ? $" and {quantity} >= {parameters.Add(lowest)}" : "";
        var maximum = range.Maximum is { } highest ? $" and {quantity} <= {parameters.Add(highest)}" : "";
        return minimum + maximum;
    }

    private static string BuildFilters(Guid? departmentId, IReadOnlyList<Guid>? categoryIds, IReadOnlyList<Guid>? brandIds, Guid[]? productIds, SqlParameterList parameters)
    {
        var filters = new StringBuilder();
        if (departmentId is { } department) filters.Append($" and p.department_id = {parameters.Add(department)}");
        if (categoryIds is { Count: > 0 }) filters.Append($" and p.category_id = any({parameters.Add(categoryIds.ToArray())})");
        if (brandIds is { Count: > 0 }) filters.Append($" and p.brand_id = any({parameters.Add(brandIds.ToArray())})");
        if (productIds is not null) filters.Append($" and p.id = any({parameters.Add(productIds)})");
        return filters.ToString();
    }

    private sealed class PreviewRow
    {
        public long TotalCount { get; set; }
        public Guid ProductId { get; set; }
        public Guid VariantId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string SizeCode { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string? CategoryName { get; set; }
        public string? BrandName { get; set; }
        public string? GenderName { get; set; }
        public string? SeasonName { get; set; }
        public decimal StockQuantity { get; set; }
        public long PurchasePrice { get; set; }
        public long CurrentSalePrice { get; set; }
        public long CurrentPrice { get; set; }
        public long NewPrice { get; set; }
    }
}
