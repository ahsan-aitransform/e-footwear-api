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
/// Product discounts as promotion rules. A rule made here covers exactly ONE store and the in-store point of sale channel (the database requires a
/// store-scoped rule to name its channel), and has product targets, so ending a product's discount in a store is deleting that target. Every rule is marked
/// created_from PRODUCT_DISCOUNT and carries the batch id of the request that made it. Rows are inserted in separate statements (rule, store, channel,
/// targets) because the rule targets' trigger reads the rule row. Rules of other origins (ordinary promotions) are never touched.
/// </summary>
public sealed class ProductDiscountRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IProductDiscountRepository
{
    private const string NaturalSizeOrder = "case when v.size_code ~ '^[0-9]+(\\.[0-9]+)?$' then v.size_code::numeric end nulls last, v.size_code";

    public async Task<Guid?> FindStorePointOfSaleChannelIdAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var channelId = await databaseContext.Database.SqlQuery<Guid>($"""
            select id as "Value" from integ.channels
            where company_id = {companyId} and kind = {ProductDiscountRuleCodes.StorePointOfSaleChannelKind} and is_active
            order by code limit 1
            """).ToListAsync(cancellationToken);
        return channelId.Count == 0 ? null : channelId[0];
    }

    public async Task<PagedResult<StockRangeDiscountRowResponse>> ListStockRangeCandidatesAsync(StockRangeDiscountRequest request, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = new SqlParameterList();
        var (candidateSql, _) = BuildCandidateSql(request, parameters);
        var take = parameters.Add(pageRequest.PageSize);
        var skip = parameters.Add(pageRequest.SkippedItemCount);

        var rows = await databaseContext.Database.SqlQueryRaw<StockRangeRow>($"""
            select count(*) over() as "TotalCount", x."ProductId", x."ProductCode", x."ProductName", x."BrandName", x."GenderName", x."SeasonName", x."LocationName",
                   x."StockQuantity", x."SalePrice", x."DiscountPercent", x."DiscountAmount"
            from ({candidateSql}) x
            order by x."ProductName", x."ProductId"
            limit {take} offset {skip}
            """, parameters.ToArray()).ToListAsync(cancellationToken);
        var items = rows.Select(row => new StockRangeDiscountRowResponse(row.ProductId, row.ProductCode, row.ProductName, row.BrandName, row.GenderName, row.SeasonName,
                                                                         row.LocationName, row.StockQuantity, row.SalePrice, row.DiscountPercent, row.DiscountAmount)).ToList();
        return new PagedResult<StockRangeDiscountRowResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<IReadOnlyList<Guid>> ListStockRangeCandidateProductIdsAsync(StockRangeDiscountRequest request, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = new SqlParameterList();
        var (candidateSql, _) = BuildCandidateSql(request, parameters);
        return await databaseContext.Database.SqlQueryRaw<Guid>($"select x.\"ProductId\" as \"Value\" from ({candidateSql}) x", parameters.ToArray()).ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyStockRangeCandidatePricedAtOrBelowAsync(StockRangeDiscountRequest request, long price, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = new SqlParameterList();
        var (candidateSql, _) = BuildCandidateSql(request, parameters);
        var limit = parameters.Add(price);
        return await databaseContext.Database
            .SqlQueryRaw<bool>($"select exists (select 1 from ({candidateSql}) x where x.\"LowestSalePrice\" <= {limit}) as \"Value\"", parameters.ToArray())
            .SingleAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductStoreDiscountRowResponse>> ListStoreDiscountRowsAsync(Guid productId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<StoreDiscountRow>($"""
            select p.id as "ProductId", p.name as "ProductName", st.id as "StoreId", st.name as "StoreName",
                   coalesce((select sum(sb.qty_on_hand) from inv.stock_balances sb
                              join catalog.variants v on v.company_id = sb.company_id and v.id = sb.variant_id and not v.is_deleted
                             where sb.company_id = p.company_id and sb.store_id = st.id and v.product_id = p.id), 0) as "StockQuantity",
                   (select max(ep.discount_percent) from promo.effective_prices ep
                      join catalog.variants v on v.company_id = ep.company_id and v.id = ep.variant_id and not v.is_deleted
                     where ep.company_id = p.company_id and ep.store_id = st.id and v.product_id = p.id and not ep.is_conflict
                       and ep.channel_id = (select ch.id from integ.channels ch where ch.company_id = p.company_id and ch.kind = 'STORE_POS' and ch.is_active order by ch.code limit 1)) as "DiscountPercent"
            from catalog.products p
            join core.stores st on st.company_id = p.company_id and st.id <> '00000000-0000-0000-0000-000000000000'::uuid and st.is_active and core.store_allowed(st.id)
            where p.company_id = {companyId} and p.id = {productId} and not p.is_deleted
            order by st.name
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new ProductStoreDiscountRowResponse(row.ProductId, row.ProductName, row.StoreId, row.StoreName, row.StockQuantity,
                                                                      row.DiscountPercent is > 0 ? row.DiscountPercent : null)).ToList();
    }

    public async Task<bool> RuleBatchExistsAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Database.SqlQuery<bool>($"""
            select exists (select 1 from promo.rules where company_id = {companyId} and created_batch_id = {batchId}) as "Value"
            """).SingleAsync(cancellationToken);
    }

    public async Task<int> RemoveProductsFromDiscountRulesAsync(IReadOnlyCollection<Guid> storeIds, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.SaveChangesAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var storeArray = storeIds.ToArray();
        var productArray = productIds.ToArray();

        var removed = await databaseContext.Database.ExecuteSqlAsync($"""
            delete from promo.rule_targets t
            using promo.rules r, promo.rule_stores rs
            where t.company_id = {companyId} and t.rule_id = r.id and r.company_id = t.company_id
              and r.created_from = {ProductDiscountRuleCodes.CreatedFromProductDiscount} and r.status = {ProductDiscountRuleCodes.StatusPublished} and not r.is_deleted
              and rs.company_id = r.company_id and rs.rule_id = r.id and rs.store_id = any({storeArray})
              and t.product_id = any({productArray})
            """, cancellationToken);

        // A rule that lost its last product has nothing left to discount.
        await databaseContext.Database.ExecuteSqlAsync($"""
            update promo.rules r set status = {ProductDiscountRuleCodes.StatusCancelled}
            where r.company_id = {companyId} and r.created_from = {ProductDiscountRuleCodes.CreatedFromProductDiscount}
              and r.status = {ProductDiscountRuleCodes.StatusPublished} and not r.is_deleted
              and not exists (select 1 from promo.rule_targets t where t.company_id = r.company_id and t.rule_id = r.id)
            """, cancellationToken);
        return removed;
    }

    public async Task<int> CreateStockRangeRuleAsync(Guid batchId, StockRangeDiscountRequest request, Guid channelId, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var productIds = (await ListStockRangeCandidateProductIdsAsync(request, cancellationToken)).ToArray();
        if (productIds.Length == 0) return 0;
        var companyId = currentUser.CompanyId;
        var storeName = await databaseContext.Database.SqlQuery<string>($"select name as \"Value\" from core.stores where company_id = {companyId} and id = {request.StoreId}")
            .SingleAsync(cancellationToken);
        var discountType = request.DiscountType!;
        var discountValue = request.DiscountValue!.Value;
        var discountText = discountType == ProductDiscountRuleCodes.DiscountTypeAmount ? $"Rs {discountValue:0} off" : $"{discountValue:0.##}% off";
        var stockText = request.MinimumStock is null && request.MaximumStock is null ? "" : $" (stock {request.MinimumStock ?? 0:0.###} to {request.MaximumStock?.ToString("0.###") ?? "any"})";
        var name = $"{discountText} at {storeName}{stockText}";

        var ruleId = await InsertRuleAsync(databaseContext, batchId, name, discountType, discountValue, request.MinimumStock, request.MaximumStock, request.StoreId, channelId,
                                           createdBy, cancellationToken);
        await InsertTargetsAsync(databaseContext, ruleId, productIds, cancellationToken);
        return productIds.Length;
    }

    public async Task CreateProductDiscountRulesAsync(Guid batchId, Guid productId, IReadOnlyCollection<Guid> storeIds, decimal discountPercent, Guid channelId, Guid createdBy,
                                                      CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var productCode = await databaseContext.Database.SqlQuery<string>($"select code as \"Value\" from catalog.products where company_id = {companyId} and id = {productId}")
            .SingleAsync(cancellationToken);
        foreach (var storeId in storeIds)
        {
            var storeName = await databaseContext.Database.SqlQuery<string>($"select name as \"Value\" from core.stores where company_id = {companyId} and id = {storeId}")
                .SingleAsync(cancellationToken);
            var name = $"Discount {discountPercent:0.##}% on {productCode} at {storeName}";
            var ruleId = await InsertRuleAsync(databaseContext, batchId, name, ProductDiscountRuleCodes.DiscountTypePercent, discountPercent, null, null, storeId, channelId, createdBy, cancellationToken);
            await InsertTargetsAsync(databaseContext, ruleId, [productId], cancellationToken);
        }
    }

    private async Task<Guid> InsertRuleAsync(ApplicationDatabaseContext databaseContext, Guid batchId, string name, string discountType, decimal discountValue, decimal? stockMinimum,
                                             decimal? stockMaximum, Guid storeId, Guid channelId, Guid createdBy, CancellationToken cancellationToken)
    {
        var companyId = currentUser.CompanyId;
        var ruleId = Guid.CreateVersion7();
        await databaseContext.Database.ExecuteSqlAsync($"""
            insert into promo.rules (company_id, id, name, discount_type, discount_value, target_type, stock_min, stock_max, starts_at, ends_at, priority, status,
                                     created_from, created_batch_id, created_by)
            values ({companyId}, {ruleId}, {name}, {discountType}, {discountValue}, {ProductDiscountRuleCodes.TargetTypeProduct},
                    {stockMinimum}, {stockMaximum}, now(), null, 0, {ProductDiscountRuleCodes.StatusPublished},
                    {ProductDiscountRuleCodes.CreatedFromProductDiscount}, {batchId}, {createdBy})
            """, cancellationToken);
        await databaseContext.Database.ExecuteSqlAsync($"insert into promo.rule_stores (company_id, rule_id, store_id) values ({companyId}, {ruleId}, {storeId})", cancellationToken);
        await databaseContext.Database.ExecuteSqlAsync($"insert into promo.rule_channels (company_id, rule_id, channel_id) values ({companyId}, {ruleId}, {channelId})", cancellationToken);
        return ruleId;
    }

    private async Task InsertTargetsAsync(ApplicationDatabaseContext databaseContext, Guid ruleId, Guid[] productIds, CancellationToken cancellationToken)
    {
        var companyId = currentUser.CompanyId;
        await databaseContext.Database.ExecuteSqlAsync($"""
            insert into promo.rule_targets (company_id, id, rule_id, product_id)
            select {companyId}, uuidv7(), {ruleId}, product_id from unnest({productIds}::uuid[]) as t(product_id)
            """, cancellationToken);
    }

    /// <summary>
    /// The matching products with at least one active size inside the stock range, when one is given (a size with no stock row counts as stock 0, like the engine
    /// does), with the store's stock, sale price and current discount. Returned as a SELECT that other statements wrap.
    /// </summary>
    private (string Sql, int ParameterCount) BuildCandidateSql(StockRangeDiscountRequest request, SqlParameterList parameters)
    {
        var company = parameters.Add(currentUser.CompanyId);
        var store = parameters.Add(request.StoreId);
        var filters = new StringBuilder();
        if (request.DepartmentId is { } departmentId) filters.Append($" and p.department_id = {parameters.Add(departmentId)}");
        if (request.CategoryIds is { Count: > 0 }) filters.Append($" and p.category_id = any({parameters.Add(request.CategoryIds.ToArray())})");
        if (request.BrandIds is { Count: > 0 }) filters.Append($" and p.brand_id = any({parameters.Add(request.BrandIds.ToArray())})");
        if (request.MinimumStock is { } minimum) filters.Append($" and coalesce(sb.qty_on_hand, 0) >= {parameters.Add(minimum)}");
        if (request.MaximumStock is { } maximum) filters.Append($" and coalesce(sb.qty_on_hand, 0) <= {parameters.Add(maximum)}");
        var sql = new StringBuilder($"""
            select p.id as "ProductId", p.code as "ProductCode", p.name as "ProductName", b.name as "BrandName", g.name as "GenderName", s.name as "SeasonName",
                   l.name as "LocationName", sum(coalesce(sb.qty_on_hand, 0)) as "StockQuantity", max(coalesce(so.sale_price, vp.sale_price)) as "SalePrice",
                   min(coalesce(so.sale_price, vp.sale_price)) as "LowestSalePrice",
                   max(ep.discount_percent) as "DiscountPercent", coalesce(max(ep.discount_amount), 0) as "DiscountAmount"
            from catalog.products p
            join catalog.variants v on v.company_id = p.company_id and v.product_id = p.id and not v.is_deleted and v.status = 'ACTIVE'
            join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
            left join inv.stock_balances sb on sb.company_id = v.company_id and sb.variant_id = v.id and sb.store_id = {store}
            left join catalog.store_price_overrides so on so.company_id = v.company_id and so.variant_id = v.id and so.store_id = {store}
            left join promo.effective_prices ep on ep.company_id = v.company_id and ep.variant_id = v.id and ep.store_id = {store}
                 and ep.channel_id = (select ch.id from integ.channels ch where ch.company_id = v.company_id and ch.kind = 'STORE_POS' and ch.is_active order by ch.code limit 1)
            left join master.brands b on b.company_id = p.company_id and b.id = p.brand_id
            left join master.genders g on g.company_id = p.company_id and g.id = p.gender_id
            left join master.seasons s on s.company_id = p.company_id and s.id = p.season_id
            left join master.locations l on l.company_id = p.company_id and l.id = p.location_id
            where p.company_id = {company} and not p.is_deleted and p.status = 'ACTIVE'{filters}
            group by p.id, p.code, p.name, b.name, g.name, s.name, l.name
            """);
        return (sql.ToString(), 0);
    }

    private sealed class StockRangeRow
    {
        public long TotalCount { get; set; }
        public Guid ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string? BrandName { get; set; }
        public string? GenderName { get; set; }
        public string? SeasonName { get; set; }
        public string? LocationName { get; set; }
        public decimal StockQuantity { get; set; }
        public long SalePrice { get; set; }
        public decimal? DiscountPercent { get; set; }
        public long DiscountAmount { get; set; }
    }

    private sealed class StoreDiscountRow
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public decimal StockQuantity { get; set; }
        public decimal? DiscountPercent { get; set; }
    }
}
