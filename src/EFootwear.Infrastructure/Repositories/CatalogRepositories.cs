using EFootwear.Application.Abstractions;
using EFootwear.Infrastructure.Persistence;

namespace EFootwear.Infrastructure.Repositories;

public sealed class CatalogRepository(Db db) : ICatalogRepository
{
    private const string ProductSelect = @"
        select p.id, p.code, p.name, p.name_ur, p.department_id, d.name as department, p.category_id, c.name as category, p.brand_id, b.name as brand,
               p.season_id, p.gender_id, p.product_type_id, p.unit_id, p.location_id, p.weight, p.description, p.tax_rate, p.status, p.row_ver,
               (select count(*) from catalog.variants v where v.company_id = p.company_id and v.product_id = p.id and not v.is_deleted) as variant_count,
               (select min(vp.sale_price) from catalog.variants v join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
                 where v.company_id = p.company_id and v.product_id = p.id and not v.is_deleted) as min_price,
               (select max(vp.sale_price) from catalog.variants v join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
                 where v.company_id = p.company_id and v.product_id = p.id and not v.is_deleted) as max_price,
               (select coalesce(sum(sb.qty_on_hand), 0) from inv.stock_balances sb join catalog.variants v on v.company_id = sb.company_id and v.id = sb.variant_id
                 where v.company_id = p.company_id and v.product_id = p.id and (@store::bigint is null or sb.store_id = @store)) as stock_qty
          from catalog.products p
          join master.departments d on d.company_id = p.company_id and d.id = p.department_id
          join master.categories c on c.company_id = p.company_id and c.id = p.category_id
          left join master.brands b on b.company_id = p.company_id and b.id = p.brand_id
         where not p.is_deleted";

    public Task<Page<Row>> ProductsAsync(ProductFilter f, PageQuery q, CancellationToken ct)
        => db.PageAsync(ProductSelect +
            @" and (@cat::bigint is null or p.category_id = @cat) and (@brand::bigint is null or p.brand_id = @brand) and (@dep::bigint is null or p.department_id = @dep)
               and (@status::text is null or p.status = @status)
               and (@q::text is null or p.code ilike @q or p.name ilike @q or p.name_ur ilike @q
                    or exists (select 1 from catalog.variants v where v.company_id = p.company_id and v.product_id = p.id and not v.is_deleted and v.barcode ilike @q))",
            "lower(p.name), p.id", new { store = f.StoreId, cat = f.CategoryId, brand = f.BrandId, dep = f.DepartmentId, status = f.Status, q = Like(q.Search) }, q, ct);

    public Task<Row?> ProductAsync(long id, CancellationToken ct) => db.RowAsync(ProductSelect + " and p.id = @id", new { id, store = (long?)null }, ct);

    public async Task<long> InsertProductAsync(long companyId, ProductInput p, string? code, CancellationToken ct)
    {
        // Without a code the row is inserted under a throw-away unique code, then renamed to P{id} (the identity is only known after insert).
        var id = await db.ScalarAsync<long>(
            @"insert into catalog.products (company_id, code, name, name_ur, department_id, category_id, brand_id, season_id, gender_id, product_type_id, unit_id, location_id,
                                            weight, description, tax_rate, status, created_by)
              values (@c, @code, @n, @u, @d, @cat, @b, @s, @g, @pt, @un, @l, @w, @desc, @tax, @st, core.current_user_id()) returning id",
            Args(p, code ?? "TMP-" + Guid.NewGuid().ToString("N"), companyId), ct);
        if (code is null) await db.ExecAsync("update catalog.products set code = 'P' || id where company_id = @c and id = @id", new { c = companyId, id }, ct);
        return id;
    }

    public async Task<bool> UpdateProductAsync(long id, ProductInput p, long? rowVer, CancellationToken ct)
        => await db.ExecAsync(
            @"update catalog.products set code = coalesce(@code, code), name = @n, name_ur = @u, department_id = @d, category_id = @cat, brand_id = @b, season_id = @s, gender_id = @g,
                     product_type_id = @pt, unit_id = @un, location_id = @l, weight = @w, description = @desc, tax_rate = @tax, status = @st
               where id = @id and not is_deleted and (@rv::bigint is null or row_ver = @rv)",
            Args(p, p.Code, 0, id, rowVer), ct) == 1;

    public Task SoftDeleteProductAsync(long id, long userId, CancellationToken ct)
        => db.ExecAsync(@"update catalog.variants set is_deleted = true, deleted_at = now(), deleted_by = @u where product_id = @id and not is_deleted;
                          update catalog.products set is_deleted = true, deleted_at = now(), deleted_by = @u where id = @id and not is_deleted", new { id, u = userId }, ct);

    public async Task<decimal> ProductStockAsync(long productId, CancellationToken ct)
        => await db.ScalarAsync<decimal?>("select sum(sb.qty_on_hand) from inv.stock_balances sb join catalog.variants v on v.company_id = sb.company_id and v.id = sb.variant_id where v.product_id = @p", new { p = productId }, ct) ?? 0;

    public Task<List<Row>> VariantsAsync(long productId, CancellationToken ct)
        => db.RowsAsync(
            @"select v.id, v.product_id, v.color_id, co.name as color, v.size_group_id, v.size_code, v.barcode, v.status, v.row_ver,
                     coalesce(vp.purchase_price, 0) as purchase_price, coalesce(vp.sale_price, 0) as sale_price,
                     (select coalesce(sum(sb.qty_on_hand), 0) from inv.stock_balances sb where sb.company_id = v.company_id and sb.variant_id = v.id) as stock_qty,
                     (select coalesce(json_agg(json_build_object('storeId', sb.store_id, 'qty', sb.qty_on_hand) order by sb.store_id), '[]'::json)::text
                        from inv.stock_balances sb where sb.company_id = v.company_id and sb.variant_id = v.id and sb.qty_on_hand <> 0) as stock_by_store_json
                from catalog.variants v
                left join master.colors co on co.company_id = v.company_id and co.id = v.color_id
                left join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
                left join master.sizes sz on sz.company_id = v.company_id and sz.size_group_id = v.size_group_id and sz.size_code = v.size_code
               where v.product_id = @p and not v.is_deleted
               order by co.name nulls first, coalesce(sz.sort_order, 0), v.size_code", new { p = productId }, ct);

    public Task<Row?> VariantAsync(long id, CancellationToken ct)
        => db.RowAsync(@"select v.id, v.product_id, p.code as product_code, p.name as product_name, v.color_id, v.size_group_id, v.size_code, v.barcode, v.status, v.row_ver
                           from catalog.variants v join catalog.products p on p.company_id = v.company_id and p.id = v.product_id where v.id = @id and not v.is_deleted", new { id }, ct);

    public Task<long> InsertVariantAsync(long companyId, long productId, VariantInput v, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into catalog.variants (company_id, product_id, color_id, size_group_id, size_code, barcode, status, created_by)
              values (@c, @p, @co, @sg, @sc, @bc, @st, core.current_user_id()) returning id",
            new { c = companyId, p = productId, co = v.ColorId, sg = v.SizeGroupId, sc = v.SizeCode, bc = v.Barcode, st = v.Status }, ct);

    public async Task<bool> UpdateVariantAsync(long id, string barcode, string status, long? rowVer, CancellationToken ct)
        => await db.ExecAsync("update catalog.variants set barcode = @b, status = @s where id = @id and not is_deleted and (@rv::bigint is null or row_ver = @rv)", new { id, b = barcode, s = status, rv = rowVer }, ct) == 1;

    public Task SoftDeleteVariantAsync(long id, long userId, CancellationToken ct)
        => db.ExecAsync("update catalog.variants set is_deleted = true, deleted_at = now(), deleted_by = @u where id = @id and not is_deleted", new { id, u = userId }, ct);

    public async Task<decimal> VariantStockAsync(long variantId, CancellationToken ct)
        => await db.ScalarAsync<decimal?>("select sum(qty_on_hand) from inv.stock_balances where variant_id = @v", new { v = variantId }, ct) ?? 0;

    public Task<Row?> PriceAsync(long variantId, CancellationToken ct)
        => db.RowAsync("select variant_id, purchase_price, sale_price, row_ver from catalog.variant_prices where variant_id = @v", new { v = variantId }, ct);

    public Task UpsertPricesAsync(long companyId, long variantId, long purchase, long sale, CancellationToken ct)
        => db.ExecAsync(
            @"insert into catalog.variant_prices (company_id, variant_id, purchase_price, sale_price, updated_by) values (@c, @v, @p, @s, core.current_user_id())
              on conflict (company_id, variant_id) do update set purchase_price = excluded.purchase_price, sale_price = excluded.sale_price, updated_at = now(), updated_by = excluded.updated_by",
            new { c = companyId, v = variantId, p = purchase, s = sale }, ct);

    public Task InsertPriceEventAsync(long companyId, long variantId, long storeId, string type, long? oldPrice, long newPrice, Guid? batch, string? reason, long userId, CancellationToken ct)
        => db.ExecAsync("insert into catalog.price_events (company_id, variant_id, store_id, price_type, old_price, new_price, batch_id, reason, changed_by) values (@c, @v, @s, @t, @o, @n, @b, @r, @u)",
            new { c = companyId, v = variantId, s = storeId, t = type, o = oldPrice, n = newPrice, b = batch, r = reason, u = userId }, ct);

    public Task UpsertStoreOverrideAsync(long companyId, long storeId, long variantId, long salePrice, long userId, CancellationToken ct)
        => db.ExecAsync(
            @"insert into catalog.store_price_overrides (company_id, store_id, variant_id, sale_price, updated_by) values (@c, @s, @v, @p, @u)
              on conflict (company_id, store_id, variant_id) do update set sale_price = excluded.sale_price, updated_at = now(), updated_by = excluded.updated_by",
            new { c = companyId, s = storeId, v = variantId, p = salePrice, u = userId }, ct);

    public Task<long?> StoreOverrideAsync(long storeId, long variantId, CancellationToken ct)
        => db.ScalarAsync<long?>("select sale_price from catalog.store_price_overrides where store_id = @s and variant_id = @v", new { s = storeId, v = variantId }, ct);

    public Task DeleteStoreOverrideAsync(long storeId, long variantId, CancellationToken ct)
        => db.ExecAsync("delete from catalog.store_price_overrides where store_id = @s and variant_id = @v", new { s = storeId, v = variantId }, ct);

    public Task<List<Row>> StoreOverridesAsync(long variantId, CancellationToken ct)
        => db.RowsAsync("select o.store_id, s.name as store, o.sale_price, o.updated_at from catalog.store_price_overrides o join core.stores s on s.company_id = o.company_id and s.id = o.store_id where o.variant_id = @v order by o.store_id", new { v = variantId }, ct);

    public Task<List<Row>> PriceEventsAsync(long? variantId, Guid? batch, int limit, CancellationToken ct)
        => db.RowsAsync(
            @"select e.id, e.variant_id, v.barcode, p.code as product_code, p.name as product_name, v.size_code, e.store_id, e.price_type, e.old_price, e.new_price, e.batch_id, e.reason, e.changed_by, e.changed_at
                from catalog.price_events e
                join catalog.variants v on v.company_id = e.company_id and v.id = e.variant_id
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
               where (@v::bigint is null or e.variant_id = @v) and (@b::uuid is null or e.batch_id = @b)
               order by e.changed_at desc, e.id desc limit @lim", new { v = variantId, b = batch, lim = Math.Clamp(limit, 1, 1000) }, ct);

    public Task<List<Row>> TargetVariantsAsync(IReadOnlyCollection<long> variantIds, IReadOnlyCollection<long> productIds, long? categoryId, long? brandId, CancellationToken ct)
        => db.RowsAsync(
            @"select v.id as variant_id, v.product_id, p.code as product_code, p.name as product_name, v.size_code, v.barcode,
                     coalesce(vp.purchase_price, 0) as purchase_price, coalesce(vp.sale_price, 0) as sale_price
                from catalog.variants v
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
                left join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
               where not v.is_deleted
                 and (v.id = any(@vids) or v.product_id = any(@pids) or (@cat::bigint is not null and p.category_id = @cat) or (@brand::bigint is not null and p.brand_id = @brand))
               order by p.code, v.id", new { vids = variantIds.ToArray(), pids = productIds.ToArray(), cat = categoryId, brand = brandId }, ct);

    /// <summary>
    /// The counter's barcode scan. An article returns the price this store sells it at right now (promotion engine output, else the store's own
    /// price, else the list price) and the stock in this store. A deal bundle returns its items.
    /// </summary>
    public async Task<Row?> LookupAsync(string barcode, long storeId, CancellationToken ct)
    {
        var v = await db.RowAsync(
            @"select 'VARIANT' as kind, v.id as variant_id, v.product_id, p.code as product_code, p.name as product_name, p.name_ur as product_name_ur, v.barcode, v.size_code, co.name as color,
                     coalesce(o.sale_price, vp.sale_price, 0) as store_list_price, coalesce(vp.sale_price, 0) as list_price, coalesce(vp.purchase_price, 0) as purchase_price,
                     case when ep.company_id is not null and (ep.valid_until is null or ep.valid_until > now()) and not ep.is_conflict then ep.sale_price end as promo_price,
                     ep.rule_id, coalesce(ep.is_conflict, false) as is_conflict, p.tax_rate, p.status,
                     coalesce((select sb.qty_on_hand from inv.stock_balances sb where sb.company_id = v.company_id and sb.store_id = @s and sb.variant_id = v.id), 0) as stock_qty
                from catalog.variants v
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
                left join master.colors co on co.company_id = v.company_id and co.id = v.color_id
                left join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
                left join catalog.store_price_overrides o on o.company_id = v.company_id and o.store_id = @s and o.variant_id = v.id
                left join promo.effective_prices ep on ep.company_id = v.company_id and ep.store_id = @s and ep.variant_id = v.id
                     and ep.channel_id = (select ch.id from integ.channels ch where ch.company_id = v.company_id and ch.code = 'POS')
               where upper(v.barcode) = upper(@b) and not v.is_deleted", new { b = barcode, s = storeId }, ct);
        if (v is not null)
        {
            var list = v.Get<long>("storeListPrice");
            var promo = v.Get<long?>("promoPrice");
            v["salePrice"] = promo is { } pr && pr < list ? pr : list;
            return v;
        }
        var b = await db.RowAsync("select 'BUNDLE' as kind, id as bundle_id, name, barcode, bundle_price, starts_at, ends_at from promo.bundles where upper(barcode) = upper(@b) and not is_deleted and status = 'PUBLISHED' and starts_at <= now() and (ends_at is null or ends_at > now())", new { b = barcode }, ct);
        if (b is null) return null;
        b["items"] = await db.RowsAsync(
            @"select bi.variant_id, v.barcode, p.name as product_name, v.size_code, bi.qty, bi.net_unit_price
                from promo.bundle_items bi join catalog.variants v on v.company_id = bi.company_id and v.id = bi.variant_id
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id where bi.bundle_id = @id order by bi.variant_id", new { id = b.Get<long>("bundleId") }, ct);
        return b;
    }

    public Task<List<Row>> SearchVariantsAsync(string q, long storeId, int limit, CancellationToken ct)
        => db.RowsAsync(
            @"select v.id as variant_id, v.barcode, p.code as product_code, p.name as product_name, v.size_code, co.name as color, coalesce(o.sale_price, vp.sale_price, 0) as list_price,
                     coalesce((select sb.qty_on_hand from inv.stock_balances sb where sb.company_id = v.company_id and sb.store_id = @s and sb.variant_id = v.id), 0) as stock_qty
                from catalog.variants v
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted and p.status = 'ACTIVE'
                left join master.colors co on co.company_id = v.company_id and co.id = v.color_id
                left join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
                left join catalog.store_price_overrides o on o.company_id = v.company_id and o.store_id = @s and o.variant_id = v.id
               where not v.is_deleted and v.status = 'ACTIVE' and (v.barcode ilike @q or p.code ilike @q or p.name ilike @q)
               order by p.name, v.size_code limit @lim", new { q = Like(q), s = storeId, lim = Math.Clamp(limit, 1, 100) }, ct);

    public Task<List<Row>> PriceBandsAsync(CancellationToken ct)
        => db.RowsAsync("select id, name, min_price, max_price, sort_order, row_ver from catalog.price_bands where not is_deleted order by min_price", null, ct);

    public Task<long> InsertPriceBandAsync(long companyId, string name, long min, long? max, int sort, CancellationToken ct)
        => db.ScalarAsync<long>("insert into catalog.price_bands (company_id, name, min_price, max_price, sort_order, created_by) values (@c, @n, @mi, @ma, @s, core.current_user_id()) returning id",
            new { c = companyId, n = name, mi = min, ma = max, s = sort }, ct);

    public async Task<bool> UpdatePriceBandAsync(long id, string name, long min, long? max, int sort, long? rowVer, CancellationToken ct)
        => await db.ExecAsync("update catalog.price_bands set name = @n, min_price = @mi, max_price = @ma, sort_order = @s where id = @id and not is_deleted and (@rv::bigint is null or row_ver = @rv)",
            new { id, n = name, mi = min, ma = max, s = sort, rv = rowVer }, ct) == 1;

    public Task DeletePriceBandAsync(long id, long userId, CancellationToken ct)
        => db.ExecAsync("update catalog.price_bands set is_deleted = true where id = @id and not is_deleted", new { id }, ct);

    internal static string? Like(string? s) => string.IsNullOrWhiteSpace(s) ? null : $"%{s.Trim()}%";

    private static object Args(ProductInput p, string? code, long companyId, long id = 0, long? rv = null) => new
    {
        c = companyId, id, rv, code, n = p.Name, u = p.NameUr, d = p.DepartmentId, cat = p.CategoryId, b = p.BrandId, s = p.SeasonId, g = p.GenderId, pt = p.ProductTypeId,
        un = p.UnitId, l = p.LocationId, w = p.Weight, desc = p.Description, tax = p.TaxRate, st = p.Status,
    };
}

public sealed class StockRepository(Db db) : IStockRepository
{
    public Task<Page<Row>> BalancesAsync(long? storeId, long? productId, long? categoryId, string? search, bool onlyPositive, PageQuery q, CancellationToken ct)
        => db.PageAsync(
            @"select sb.store_id, s.name as store, sb.variant_id, v.barcode, p.id as product_id, p.code as product_code, p.name as product_name, co.name as color, v.size_code,
                     sb.qty_on_hand, sb.avg_cost, (sb.qty_on_hand * sb.avg_cost)::bigint as stock_value, sb.last_receipt_at
                from inv.stock_balances sb
                join core.stores s on s.company_id = sb.company_id and s.id = sb.store_id
                join catalog.variants v on v.company_id = sb.company_id and v.id = sb.variant_id
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
                left join master.colors co on co.company_id = v.company_id and co.id = v.color_id
               where (@st::bigint is null or sb.store_id = @st) and (@p::bigint is null or p.id = @p) and (@cat::bigint is null or p.category_id = @cat)
                 and (not @pos or sb.qty_on_hand > 0) and (@q::text is null or p.code ilike @q or p.name ilike @q or v.barcode ilike @q)",
            "p.name, v.size_code, sb.store_id", new { st = storeId, p = productId, cat = categoryId, pos = onlyPositive, q = CatalogRepository.Like(search) }, q, ct);

    /// <summary>Size-wise grid for one article: rows are variants, the JSON column holds the quantity in every store the caller may see.</summary>
    public Task<List<Row>> BySizeAsync(long productId, CancellationToken ct)
        => db.RowsAsync(
            @"select v.id as variant_id, v.size_code, co.name as color, v.barcode,
                     (select coalesce(json_agg(json_build_object('storeId', sb.store_id, 'qty', sb.qty_on_hand) order by sb.store_id), '[]'::json)::text
                        from inv.stock_balances sb where sb.company_id = v.company_id and sb.variant_id = v.id) as stores_json,
                     (select coalesce(sum(sb.qty_on_hand), 0) from inv.stock_balances sb where sb.company_id = v.company_id and sb.variant_id = v.id) as total
                from catalog.variants v left join master.colors co on co.company_id = v.company_id and co.id = v.color_id
               where v.product_id = @p and not v.is_deleted order by co.name nulls first, v.size_code", new { p = productId }, ct);

    public Task<Page<Row>> LedgerAsync(long? variantId, long? storeId, DateOnly? from, DateOnly? to, string? docType, PageQuery q, CancellationToken ct)
        => db.PageAsync(
            @"select l.id, l.txn_date, l.txn_at, l.store_id, s.name as store, l.variant_id, v.barcode, p.name as product_name, v.size_code, l.doc_type, l.doc_id, l.qty, l.unit_cost, l.qty_after
                from inv.stock_ledger l
                join core.stores s on s.company_id = l.company_id and s.id = l.store_id
                join catalog.variants v on v.company_id = l.company_id and v.id = l.variant_id
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
               where (@v::bigint is null or l.variant_id = @v) and (@st::bigint is null or l.store_id = @st)
                 and (@f::date is null or l.txn_date >= @f) and (@t::date is null or l.txn_date <= @t) and (@dt::text is null or l.doc_type = @dt)",
            "l.txn_at desc, l.id desc", new { v = variantId, st = storeId, f = from, t = to, dt = docType }, q, ct);

    public Task<long> ChannelIdAsync(string code, CancellationToken ct)
        => db.ScalarAsync<long>("select id from integ.channels where code = @c", new { c = code }, ct);
}

public sealed class PromoRepository(Db db) : IPromoRepository
{
    public Task<Page<Row>> RulesAsync(string? status, PageQuery q, CancellationToken ct)
        => db.PageAsync(
            @"select r.id, r.name, r.discount_type, r.discount_value, r.target_type, r.stock_min, r.stock_max, r.starts_at, r.ends_at, r.priority, r.status, r.row_ver,
                     (select count(*) from promo.rule_targets t where t.company_id = r.company_id and t.rule_id = r.id) as target_count
                from promo.rules r where not r.is_deleted and (@st::text is null or r.status = @st) and (@q::text is null or r.name ilike @q)",
            "r.starts_at desc, r.id desc", new { st = status, q = CatalogRepository.Like(q.Search) }, q, ct);

    public async Task<Row?> RuleAsync(long id, CancellationToken ct)
    {
        var r = await db.RowAsync("select id, name, discount_type, discount_value, target_type, stock_min, stock_max, starts_at, ends_at, priority, status, row_ver from promo.rules where id = @id and not is_deleted", new { id }, ct);
        if (r is null) return null;
        r["targets"] = await db.RowsAsync(
            @"select t.id, t.category_id, c.name as category, t.product_id, p.code as product_code, p.name as product_name, t.variant_id, v.barcode, t.discount_override
                from promo.rule_targets t
                left join master.categories c on c.company_id = t.company_id and c.id = t.category_id
                left join catalog.products p on p.company_id = t.company_id and p.id = t.product_id
                left join catalog.variants v on v.company_id = t.company_id and v.id = t.variant_id where t.rule_id = @id order by t.id", new { id }, ct);
        r["storeIds"] = await db.QueryAsync<long>("select store_id from promo.rule_stores where rule_id = @id order by 1", new { id }, ct);
        r["channelIds"] = await db.QueryAsync<long>("select channel_id from promo.rule_channels where rule_id = @id order by 1", new { id }, ct);
        return r;
    }

    public Task<long> InsertRuleAsync(long companyId, RuleInput r, CancellationToken ct)
        => db.ScalarAsync<long>(
            @"insert into promo.rules (company_id, name, discount_type, discount_value, target_type, stock_min, stock_max, starts_at, ends_at, priority, created_by)
              values (@c, @n, @dt, @dv, @tt, @smin, @smax, @sa, @ea, @pr, core.current_user_id()) returning id", Args(companyId, r), ct);

    public async Task<bool> UpdateRuleAsync(long id, RuleInput r, long? rowVer, CancellationToken ct)
        => await db.ExecAsync(
            @"update promo.rules set name = @n, discount_type = @dt, discount_value = @dv, target_type = @tt, stock_min = @smin, stock_max = @smax, starts_at = @sa, ends_at = @ea, priority = @pr
               where id = @id and not is_deleted and (@rv::bigint is null or row_ver = @rv)", Args(0, r, id, rowVer), ct) == 1;

    public Task SetRuleStatusAsync(long id, string status, CancellationToken ct) => db.ExecAsync("update promo.rules set status = @s where id = @id and not is_deleted", new { id, s = status }, ct);

    public Task SoftDeleteRuleAsync(long id, long userId, CancellationToken ct)
        => db.ExecAsync("update promo.rules set is_deleted = true, deleted_at = now(), deleted_by = @u, status = 'CANCELLED' where id = @id and not is_deleted", new { id, u = userId }, ct);

    public async Task ReplaceRuleScopeAsync(long companyId, long ruleId, IReadOnlyCollection<RuleTarget> targets, IReadOnlyCollection<long> stores, IReadOnlyCollection<long> channels, CancellationToken ct)
    {
        await db.ExecAsync("delete from promo.rule_targets where company_id = @c and rule_id = @r; delete from promo.rule_stores where company_id = @c and rule_id = @r; delete from promo.rule_channels where company_id = @c and rule_id = @r", new { c = companyId, r = ruleId }, ct);
        foreach (var t in targets)
            await db.ExecAsync("insert into promo.rule_targets (company_id, rule_id, category_id, product_id, variant_id, discount_override) values (@c, @r, @cat, @p, @v, @o)",
                new { c = companyId, r = ruleId, cat = t.CategoryId, p = t.ProductId, v = t.VariantId, o = t.DiscountOverride }, ct);
        foreach (var s in stores) await db.ExecAsync("insert into promo.rule_stores (company_id, rule_id, store_id) values (@c, @r, @s)", new { c = companyId, r = ruleId, s }, ct);
        foreach (var ch in channels) await db.ExecAsync("insert into promo.rule_channels (company_id, rule_id, channel_id) values (@c, @r, @ch)", new { c = companyId, r = ruleId, ch }, ct);
    }

    public Task<Page<Row>> BundlesAsync(PageQuery q, CancellationToken ct)
        => db.PageAsync("select b.id, b.name, b.barcode, b.bundle_price, b.starts_at, b.ends_at, b.status, b.row_ver, (select count(*) from promo.bundle_items i where i.company_id = b.company_id and i.bundle_id = b.id) as item_count from promo.bundles b where not b.is_deleted and (@q::text is null or b.name ilike @q or b.barcode ilike @q)",
            "b.starts_at desc, b.id desc", new { q = CatalogRepository.Like(q.Search) }, q, ct);

    public async Task<Row?> BundleAsync(long id, CancellationToken ct)
    {
        var b = await db.RowAsync("select id, name, barcode, bundle_price, starts_at, ends_at, status, row_ver from promo.bundles where id = @id and not is_deleted", new { id }, ct);
        if (b is null) return null;
        b["items"] = await db.RowsAsync("select i.variant_id, v.barcode, p.name as product_name, v.size_code, i.qty, i.net_unit_price from promo.bundle_items i join catalog.variants v on v.company_id = i.company_id and v.id = i.variant_id join catalog.products p on p.company_id = v.company_id and p.id = v.product_id where i.bundle_id = @id order by i.variant_id", new { id }, ct);
        return b;
    }

    public Task<long> InsertBundleAsync(long companyId, string name, string barcode, long price, DateTimeOffset starts, DateTimeOffset? ends, CancellationToken ct)
        => db.ScalarAsync<long>("insert into promo.bundles (company_id, name, barcode, bundle_price, starts_at, ends_at, created_by) values (@c, @n, @b, @p, @s, @e, core.current_user_id()) returning id",
            new { c = companyId, n = name, b = barcode, p = price, s = starts, e = ends }, ct);

    public async Task ReplaceBundleItemsAsync(long companyId, long bundleId, IReadOnlyCollection<BundleItemInput> items, CancellationToken ct)
    {
        await db.ExecAsync("delete from promo.bundle_items where company_id = @c and bundle_id = @b", new { c = companyId, b = bundleId }, ct);
        foreach (var i in items)
            await db.ExecAsync("insert into promo.bundle_items (company_id, bundle_id, variant_id, qty, net_unit_price) values (@c, @b, @v, @q, @p)", new { c = companyId, b = bundleId, v = i.VariantId, q = i.Qty, p = i.NetUnitPrice }, ct);
    }

    public Task SetBundleStatusAsync(long id, string status, CancellationToken ct) => db.ExecAsync("update promo.bundles set status = @s where id = @id and not is_deleted", new { id, s = status }, ct);
    public Task SoftDeleteBundleAsync(long id, long userId, CancellationToken ct) => db.ExecAsync("update promo.bundles set is_deleted = true, deleted_at = now(), deleted_by = @u, status = 'CANCELLED' where id = @id and not is_deleted", new { id, u = userId }, ct);

    public Task<List<Row>> ChannelsAsync(CancellationToken ct) => db.RowsAsync("select id, code, name, kind, is_active from integ.channels order by id", null, ct);

    public Task<List<Row>> ConflictsAsync(CancellationToken ct)
        => db.RowsAsync(
            @"select g.id, g.channel_id, ch.code as channel, g.store_id, s.name as store, g.variant_id, v.barcode, p.name as product_name, v.size_code, g.candidate_key, g.created_at,
                     (select coalesce(json_agg(json_build_object('ruleId', c.rule_id, 'rule', r.name, 'salePrice', c.sale_price, 'discountPercent', c.discount_percent) order by c.sale_price), '[]'::json)::text
                        from promo.conflict_candidates c join promo.rules r on r.company_id = c.company_id and r.id = c.rule_id where c.company_id = g.company_id and c.group_id = g.id) as candidates_json
                from promo.conflict_groups g
                join integ.channels ch on ch.company_id = g.company_id and ch.id = g.channel_id
                join core.stores s on s.company_id = g.company_id and s.id = g.store_id
                join catalog.variants v on v.company_id = g.company_id and v.id = g.variant_id
                join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
               where g.status = 'OPEN' order by g.id", null, ct);

    public Task<Row?> ConflictAsync(long id, CancellationToken ct)
        => db.RowAsync("select g.id, g.status, g.variant_id, g.store_id, (select array_agg(c.rule_id) from promo.conflict_candidates c where c.company_id = g.company_id and c.group_id = g.id) as candidate_rules from promo.conflict_groups g where g.id = @id", new { id }, ct);

    public Task ResolveConflictAsync(long id, long ruleId, long userId, CancellationToken ct)
        => db.ExecAsync("update promo.conflict_groups set status = 'RESOLVED', chosen_rule_id = @r, resolved_by = @u, resolved_at = now() where id = @id and status = 'OPEN'", new { id, r = ruleId, u = userId }, ct);

    public Task<long> RebuildEffectivePricesAsync(long companyId, CancellationToken ct)
        => db.ScalarAsync<long>("select promo.rebuild_effective_prices(@c)", new { c = companyId }, ct);

    /// <summary>True when a rule has just started or ended, a price has expired, or a rule depends on stock level (which changes with every sale).</summary>
    public async Task<bool> NeedsRefreshAsync(long companyId, CancellationToken ct)
        => await db.ScalarAsync<bool>(
            @"select exists (select 1 from promo.rules r where r.company_id = @c and r.status = 'PUBLISHED' and not r.is_deleted
                              and ((r.starts_at <= now() and r.starts_at > now() - interval '3 minutes') or (r.ends_at <= now() and r.ends_at > now() - interval '3 minutes')
                                   or r.stock_min is not null or r.stock_max is not null))
                  or exists (select 1 from promo.effective_prices e where e.company_id = @c and e.valid_until is not null and e.valid_until <= now())", new { c = companyId }, ct);

    public Task<List<long>> ActiveCompanyIdsAsync(CancellationToken ct) => db.QueryAsync<long>("select id from platform.companies where status = 'ACTIVE' order by id", null, ct);

    private static object Args(long companyId, RuleInput r, long id = 0, long? rv = null) => new
    {
        c = companyId, id, rv, n = r.Name, dt = r.DiscountType, dv = r.DiscountValue, tt = r.TargetType, smin = r.StockMin, smax = r.StockMax, sa = r.StartsAt, ea = r.EndsAt, pr = r.Priority,
    };
}
