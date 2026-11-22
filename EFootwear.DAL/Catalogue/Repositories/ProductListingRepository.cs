using System.Text;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Catalogue.Repositories;

/// <summary>
/// The read models of the catalogue screens, as set-based SQL. Filters and the sort column are chosen in code from fixed text (a whitelist), and every
/// value travels as a parameter. Stock comes from inv.stock_balances (row-level security hides stores the caller may not see); the discount comes from the
/// pricing engine's promo.effective_prices for the in-store point of sale channel.
/// </summary>
public sealed class ProductListingRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IProductListingRepository
{
    private const string NaturalSizeOrder =
        "case when v.size_code ~ '^[0-9]+(\\.[0-9]+)?$' then v.size_code::numeric end nulls last, v.size_code";

    private const string PointOfSaleChannelSubquery =
        "(select ch.id from integ.channels ch where ch.company_id = v.company_id and ch.kind = 'STORE_POS' and ch.is_active order by ch.code limit 1)";

    private static readonly Dictionary<string, string> VariantSortExpressions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["productName"] = "p.name", ["productCode"] = "p.code", ["barcode"] = "v.barcode", ["sizeCode"] = "v.size_code", ["unit"] = "u.name",
        ["stock"] = "stock.quantity", ["purchasePrice"] = "vp.purchase_price", ["department"] = "d.name", ["category"] = "c.name", ["brand"] = "b.name",
        ["gender"] = "g.name", ["season"] = "s.name", ["color"] = "col.name", ["status"] = "p.status",
    };

    public async Task<PagedResult<ProductVariantListItemResponse>> ListVariantRowsAsync(ProductListFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = new SqlParameterList();
        var company = parameters.Add(currentUser.CompanyId);
        var store = filter.StoreId is { } storeId ? parameters.Add(storeId) : null;

        var storePriceJoin = store is null ? "" : $"left join catalog.store_price_overrides so on so.company_id = v.company_id and so.variant_id = v.id and so.store_id = {store}";
        var effectivePriceJoin = store is null ? "" :
            $"left join promo.effective_prices ep on ep.company_id = v.company_id and ep.variant_id = v.id and ep.store_id = {store} and ep.channel_id = {PointOfSaleChannelSubquery}";
        var salePrice = store is null ? "vp.sale_price" : "coalesce(so.sale_price, vp.sale_price)";
        var stockStoreCondition = store is null ? "" : $"and sb.store_id = {store}";
        var discountPercent = store is null ? "null::numeric" : "ep.discount_percent";
        var discountAmount = store is null ? "0::bigint" : "coalesce(ep.discount_amount, 0)";

        var where = new StringBuilder($"v.company_id = {company} and not v.is_deleted and not p.is_deleted");
        if (filter.OnlyActive) where.Append(" and p.status = 'ACTIVE' and v.status = 'ACTIVE'");
        if (filter.DepartmentId is { } departmentId) where.Append($" and p.department_id = {parameters.Add(departmentId)}");
        if (filter.CategoryId is { } categoryId) where.Append($" and p.category_id = {parameters.Add(categoryId)}");
        if (filter.BrandId is { } brandId) where.Append($" and p.brand_id = {parameters.Add(brandId)}");
        if (filter.GenderId is { } genderId) where.Append($" and p.gender_id = {parameters.Add(genderId)}");
        if (filter.SeasonId is { } seasonId) where.Append($" and p.season_id = {parameters.Add(seasonId)}");
        if (filter.ColorId is { } colorId) where.Append($" and v.color_id = {parameters.Add(colorId)}");
        if (pageRequest.SearchText is { } searchText)
            where.Append(" and " + BuildSearchCondition(filter, searchText, salePrice, parameters));

        var orderExpression = pageRequest.SortField is { } sortField && VariantSortExpressions.TryGetValue(sortField, out var sortExpression)
            ? $"{sortExpression} {(pageRequest.SortDirection == SortDirection.Descending ? "desc" : "asc")} nulls last, p.name, p.id, {NaturalSizeOrder}"
            : $"p.name, p.id, {NaturalSizeOrder}";
        if (pageRequest.SortField == "salePrice")
            orderExpression = $"{salePrice} {(pageRequest.SortDirection == SortDirection.Descending ? "desc" : "asc")}, p.name, p.id, {NaturalSizeOrder}";
        var take = parameters.Add(pageRequest.PageSize);
        var skip = parameters.Add(pageRequest.SkippedItemCount);

        var rows = await databaseContext.Database.SqlQueryRaw<VariantListRow>($"""
            select count(*) over() as "TotalCount", p.id as "ProductId", v.id as "VariantId", p.code as "ProductCode", v.barcode as "Barcode",
                   p.name as "ProductName", p.name_ur as "ProductNameUrdu", u.name as "UnitName", v.size_code as "SizeCode", col.name as "ColorName",
                   coalesce(stock.quantity, 0) as "StockQuantity", vp.purchase_price as "PurchasePrice", {salePrice} as "SalePrice",
                   {discountPercent} as "DiscountPercent", {discountAmount} as "DiscountAmount",
                   d.name as "DepartmentName", c.name as "CategoryName", b.name as "BrandName", g.name as "GenderName", s.name as "SeasonName", v.status as "Status"
            from catalog.variants v
            join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
            join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
            join master.departments d on d.company_id = p.company_id and d.id = p.department_id
            join master.categories c on c.company_id = p.company_id and c.id = p.category_id
            left join master.brands b on b.company_id = p.company_id and b.id = p.brand_id
            left join master.genders g on g.company_id = p.company_id and g.id = p.gender_id
            left join master.seasons s on s.company_id = p.company_id and s.id = p.season_id
            left join master.units u on u.company_id = p.company_id and u.id = p.unit_id
            left join master.colors col on col.company_id = v.company_id and col.id = v.color_id
            {storePriceJoin}
            {effectivePriceJoin}
            left join lateral (select sum(sb.qty_on_hand) as quantity from inv.stock_balances sb
                               where sb.company_id = v.company_id and sb.variant_id = v.id {stockStoreCondition}) stock on true
            where {where}
            order by {orderExpression}
            limit {take} offset {skip}
            """, parameters.ToArray()).ToListAsync(cancellationToken);

        var items = rows.Select(ToResponse).ToList();
        return new PagedResult<ProductVariantListItemResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<VariantPriceLookupResponse?> FindPriceLookupAsync(string barcode, Guid? storeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = new SqlParameterList();
        var company = parameters.Add(currentUser.CompanyId);
        var barcodeParameter = parameters.Add(barcode);
        var store = storeId is { } id ? parameters.Add(id) : null;

        var storePriceJoin = store is null ? "" : $"left join catalog.store_price_overrides so on so.company_id = v.company_id and so.variant_id = v.id and so.store_id = {store}";
        var effectivePriceJoin = store is null ? "" :
            $"left join promo.effective_prices ep on ep.company_id = v.company_id and ep.variant_id = v.id and ep.store_id = {store} and ep.channel_id = {PointOfSaleChannelSubquery}";
        var storePrice = store is null ? "vp.sale_price" : "coalesce(so.sale_price, vp.sale_price)";
        var effectivePrice = store is null ? "vp.sale_price" : "coalesce(ep.sale_price, coalesce(so.sale_price, vp.sale_price))";
        var discountAmount = store is null ? "0::bigint" : "coalesce(ep.discount_amount, 0)";
        var discountPercent = store is null ? "null::numeric" : "ep.discount_percent";
        var isConflict = store is null ? "false" : "coalesce(ep.is_conflict, false)";
        var stockStoreCondition = store is null ? "" : $"and sb.store_id = {store}";

        var row = await databaseContext.Database.SqlQueryRaw<PriceLookupRow>($"""
            select v.id as "VariantId", p.id as "ProductId", p.code as "ProductCode", v.barcode as "Barcode", p.name as "ProductName", p.name_ur as "ProductNameUrdu",
                   v.size_code as "SizeCode", col.name as "ColorName", b.name as "BrandName", coalesce(stock.quantity, 0) as "StockQuantity",
                   vp.sale_price as "ListPrice", {storePrice} as "StorePrice", {effectivePrice} as "EffectiveSalePrice",
                   {discountAmount} as "DiscountAmount", {discountPercent} as "DiscountPercent", {isConflict} as "IsPriceInConflict"
            from catalog.variants v
            join catalog.products p on p.company_id = v.company_id and p.id = v.product_id
            join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
            left join master.brands b on b.company_id = p.company_id and b.id = p.brand_id
            left join master.colors col on col.company_id = v.company_id and col.id = v.color_id
            {storePriceJoin}
            {effectivePriceJoin}
            left join lateral (select sum(sb.qty_on_hand) as quantity from inv.stock_balances sb
                               where sb.company_id = v.company_id and sb.variant_id = v.id {stockStoreCondition}) stock on true
            where v.company_id = {company} and v.barcode = {barcodeParameter} and not v.is_deleted and not p.is_deleted
            """, parameters.ToArray()).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new VariantPriceLookupResponse(row.VariantId, row.ProductId, row.ProductCode, row.Barcode, row.ProductName, row.ProductNameUrdu,
            row.SizeCode, row.ColorName, row.BrandName, row.StockQuantity, row.ListPrice, row.StorePrice, row.EffectiveSalePrice, row.DiscountAmount,
            row.DiscountPercent, row.IsPriceInConflict, storeId);
    }

    public async Task<Guid?> FindProductIdByBarcodeAsync(string barcode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.ProductVariants.AsNoTracking()
            .Where(variant => variant.CompanyId == companyId && variant.Barcode == barcode && !variant.IsDeleted)
            .Select(variant => (Guid?)variant.ProductId).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResult<ProductStockByStoreRowResponse>> ListStockByStoreAsync(PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = new SqlParameterList();
        var company = parameters.Add(currentUser.CompanyId);
        var search = "";
        if (pageRequest.SearchText is { } searchText)
        {
            var pattern = parameters.Add(LikePattern.Contains(searchText));
            search = $"and (p.name ilike {pattern} escape '\\' or p.code ilike {pattern} escape '\\' or v.barcode ilike {pattern} escape '\\')";
        }
        var take = parameters.Add(pageRequest.PageSize);
        var skip = parameters.Add(pageRequest.SkippedItemCount);

        var rows = await databaseContext.Database.SqlQueryRaw<StockByStoreRow>($"""
            select count(*) over() as "TotalCount", p.id as "ProductId", p.code as "ProductCode", p.name as "ProductName", st.id as "StoreId", st.name as "StoreName",
                   v.size_code as "SizeCode", v.barcode as "Barcode", sb.qty_on_hand as "StockQuantity"
            from inv.stock_balances sb
            join catalog.variants v on v.company_id = sb.company_id and v.id = sb.variant_id and not v.is_deleted
            join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
            join core.stores st on st.company_id = sb.company_id and st.id = sb.store_id
            where sb.company_id = {company} and sb.qty_on_hand > 0 and core.store_allowed(sb.store_id) {search}
            order by p.name, p.id, st.name, {NaturalSizeOrder}
            limit {take} offset {skip}
            """, parameters.ToArray()).ToListAsync(cancellationToken);
        var items = rows.Select(row => new ProductStockByStoreRowResponse(row.ProductId, row.ProductCode, row.ProductName, row.StoreId, row.StoreName,
                                                                          row.SizeCode, row.Barcode, row.StockQuantity)).ToList();
        return new PagedResult<ProductStockByStoreRowResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<PagedResult<ProductReportRowResponse>> ListProductReportAsync(ProductReportFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = new SqlParameterList();
        var company = parameters.Add(currentUser.CompanyId);
        var where = new StringBuilder($"p.company_id = {company} and not p.is_deleted");
        if (filter.BrandId is { } brandId) where.Append($" and p.brand_id = {parameters.Add(brandId)}");
        if (filter.SeasonId is { } seasonId) where.Append($" and p.season_id = {parameters.Add(seasonId)}");
        if (filter.ColorId is { } colorId)
            where.Append($" and exists (select 1 from catalog.variants cv where cv.company_id = p.company_id and cv.product_id = p.id and not cv.is_deleted and cv.color_id = {parameters.Add(colorId)})");
        if (pageRequest.SearchText is { } searchText)
        {
            var pattern = parameters.Add(LikePattern.Contains(searchText));
            where.Append($" and (p.name ilike {pattern} escape '\\' or p.code ilike {pattern} escape '\\')");
        }
        var take = parameters.Add(pageRequest.PageSize);
        var skip = parameters.Add(pageRequest.SkippedItemCount);

        var rows = await databaseContext.Database.SqlQueryRaw<ProductReportRow>($"""
            select count(*) over() as "TotalCount", p.id as "ProductId", p.code as "ProductCode", p.name as "ProductName",
                   max(vp.purchase_price) as "PurchasePrice", max(coalesce(so.sale_price, vp.sale_price)) as "SalePrice",
                   b.name as "BrandName", s.name as "SeasonName",
                   (select string_agg(distinct col.name, ', ' order by col.name) from catalog.variants cv
                      join master.colors col on col.company_id = cv.company_id and col.id = cv.color_id
                     where cv.company_id = p.company_id and cv.product_id = p.id and not cv.is_deleted) as "ColorName",
                   st.id as "StoreId", st.name as "StoreName", coalesce(sum(sb.qty_on_hand), 0) as "StockQuantity"
            from catalog.products p
            join catalog.variants v on v.company_id = p.company_id and v.product_id = p.id and not v.is_deleted
            join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
            join core.stores st on st.company_id = p.company_id and st.id <> '00000000-0000-0000-0000-000000000000'::uuid and st.is_active and core.store_allowed(st.id)
            left join catalog.store_price_overrides so on so.company_id = v.company_id and so.variant_id = v.id and so.store_id = st.id
            left join inv.stock_balances sb on sb.company_id = v.company_id and sb.variant_id = v.id and sb.store_id = st.id
            left join master.brands b on b.company_id = p.company_id and b.id = p.brand_id
            left join master.seasons s on s.company_id = p.company_id and s.id = p.season_id
            where {where}
            group by p.company_id, p.id, p.code, p.name, b.name, s.name, st.id, st.name
            order by p.name, p.id, st.name
            limit {take} offset {skip}
            """, parameters.ToArray()).ToListAsync(cancellationToken);
        var items = rows.Select(row => new ProductReportRowResponse(row.ProductId, row.ProductCode, row.ProductName, row.PurchasePrice, row.SalePrice, row.BrandName,
                                                                    row.SeasonName, row.ColorName, row.StoreId, row.StoreName, row.StockQuantity)).ToList();
        return new PagedResult<ProductReportRowResponse>(items, pageRequest.PageNumber, pageRequest.PageSize, rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task<BarcodeLabelData?> FindLabelDataAsync(Guid variantId, Guid? storeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var parameters = new SqlParameterList();
        var company = parameters.Add(currentUser.CompanyId);
        var variant = parameters.Add(variantId);
        var store = storeId is { } id ? parameters.Add(id) : null;
        var storePriceJoin = store is null ? "" : $"left join catalog.store_price_overrides so on so.company_id = v.company_id and so.variant_id = v.id and so.store_id = {store}";
        var salePrice = store is null ? "vp.sale_price" : "coalesce(so.sale_price, vp.sale_price)";

        return await databaseContext.Database.SqlQueryRaw<LabelRow>($"""
            select v.id as "VariantId", v.barcode as "Barcode", p.code as "ProductCode", p.name as "ProductName", p.name_ur as "ProductNameUrdu",
                   v.size_code as "SizeCode", col.name as "ColorName", b.name as "BrandName", {salePrice} as "SalePrice"
            from catalog.variants v
            join catalog.products p on p.company_id = v.company_id and p.id = v.product_id and not p.is_deleted
            join catalog.variant_prices vp on vp.company_id = v.company_id and vp.variant_id = v.id
            left join master.brands b on b.company_id = p.company_id and b.id = p.brand_id
            left join master.colors col on col.company_id = v.company_id and col.id = v.color_id
            {storePriceJoin}
            where v.company_id = {company} and v.id = {variant} and not v.is_deleted
            """, parameters.ToArray())
            .Select(row => new BarcodeLabelData(row.VariantId, row.Barcode, row.ProductCode, row.ProductName, row.ProductNameUrdu, row.SizeCode, row.ColorName,
                                                row.BrandName, row.SalePrice))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string BuildSearchCondition(ProductListFilter filter, string searchText, string salePriceExpression, SqlParameterList parameters)
    {
        var pattern = parameters.Add(filter.MatchMode switch
        {
            ProductMatchModeCodes.Start => LikePattern.StartsWith(searchText),
            ProductMatchModeCodes.End => LikePattern.EndsWith(searchText),
            _ => LikePattern.Contains(searchText),
        });
        string Like(string column) => $"{column} ilike {pattern} escape '\\'";

        return filter.SearchField switch
        {
            ProductSearchFieldCodes.Name => "(" + Like("p.name") + (filter.IncludeDescription ? " or " + Like("p.description") : "") + ")",
            ProductSearchFieldCodes.Barcode => Like("v.barcode"),
            ProductSearchFieldCodes.Department => Like("d.name"),
            ProductSearchFieldCodes.Category => Like("c.name"),
            ProductSearchFieldCodes.Brand => Like("b.name"),
            ProductSearchFieldCodes.Color => Like("col.name"),
            ProductSearchFieldCodes.SalePrice => Like($"({salePriceExpression})::text"),
            _ => "(" + Like("p.name") + " or " + Like("p.code") + " or " + Like("v.barcode") + " or " + Like("p.name_ur")
                 + (filter.IncludeDescription ? " or " + Like("p.description") : "") + ")",
        };
    }

    private static ProductVariantListItemResponse ToResponse(VariantListRow row)
    {
        var netPrice = ProductPricingFormulas.NetPrice(row.SalePrice, row.DiscountAmount);
        return new ProductVariantListItemResponse(row.ProductId, row.VariantId, row.ProductCode, row.Barcode, row.ProductName, row.ProductNameUrdu, row.UnitName,
            row.SizeCode, row.ColorName, row.StockQuantity, row.PurchasePrice, row.SalePrice, row.DiscountPercent, row.DiscountAmount, netPrice,
            ProductPricingFormulas.MarginPercent(netPrice, row.PurchasePrice), ProductPricingFormulas.MarkupPercent(netPrice, row.PurchasePrice),
            row.DepartmentName, row.CategoryName, row.BrandName, row.GenderName, row.SeasonName, row.Status);
    }

    private sealed class VariantListRow
    {
        public long TotalCount { get; set; }
        public Guid ProductId { get; set; }
        public Guid VariantId { get; set; }
        public string ProductCode { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string? ProductNameUrdu { get; set; }
        public string? UnitName { get; set; }
        public string SizeCode { get; set; } = "";
        public string? ColorName { get; set; }
        public decimal StockQuantity { get; set; }
        public long PurchasePrice { get; set; }
        public long SalePrice { get; set; }
        public decimal? DiscountPercent { get; set; }
        public long DiscountAmount { get; set; }
        public string DepartmentName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string? BrandName { get; set; }
        public string? GenderName { get; set; }
        public string? SeasonName { get; set; }
        public string Status { get; set; } = "";
    }

    private sealed class PriceLookupRow
    {
        public Guid VariantId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string? ProductNameUrdu { get; set; }
        public string SizeCode { get; set; } = "";
        public string? ColorName { get; set; }
        public string? BrandName { get; set; }
        public decimal StockQuantity { get; set; }
        public long ListPrice { get; set; }
        public long StorePrice { get; set; }
        public long EffectiveSalePrice { get; set; }
        public long DiscountAmount { get; set; }
        public decimal? DiscountPercent { get; set; }
        public bool IsPriceInConflict { get; set; }
    }

    private sealed class StockByStoreRow
    {
        public long TotalCount { get; set; }
        public Guid ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public string SizeCode { get; set; } = "";
        public string Barcode { get; set; } = "";
        public decimal StockQuantity { get; set; }
    }

    private sealed class ProductReportRow
    {
        public long TotalCount { get; set; }
        public Guid ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public long PurchasePrice { get; set; }
        public long SalePrice { get; set; }
        public string? BrandName { get; set; }
        public string? SeasonName { get; set; }
        public string? ColorName { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public decimal StockQuantity { get; set; }
    }

    private sealed class LabelRow
    {
        public Guid VariantId { get; set; }
        public string Barcode { get; set; } = "";
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string? ProductNameUrdu { get; set; }
        public string SizeCode { get; set; } = "";
        public string? ColorName { get; set; }
        public string? BrandName { get; set; }
        public long SalePrice { get; set; }
    }
}
