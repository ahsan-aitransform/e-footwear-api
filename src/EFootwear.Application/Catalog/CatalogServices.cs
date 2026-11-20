using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using EFootwear.Application.Abstractions;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;

namespace EFootwear.Application.Catalog;

/// <summary>
/// Keeps promo.effective_prices (what a POS line reads) in step with prices, stock and promotions. The database function rebuilds a whole
/// company, so changes are normally batched: <see cref="MarkDirty"/> asks the background worker, <see cref="RebuildAsync"/> does it now
/// (promotion publish/cancel, where the user expects the new price immediately). A lookup never depends on it: with no row it falls back
/// to the list price.
/// </summary>
public sealed class PriceRefresher(IUnitOfWorkFactory uows)
{
    private readonly ConcurrentDictionary<long, DateTimeOffset> _dirty = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _locks = new();

    public void MarkDirty(long companyId) => _dirty[companyId] = DateTimeOffset.UtcNow;

    public async Task RebuildAsync(long companyId, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(companyId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            _dirty.TryRemove(companyId, out _);
            await using var uow = await uows.BeginSystemAsync(companyId, ct);
            await uow.Promos.RebuildEffectivePricesAsync(companyId, ct);
            await uow.CommitAsync(ct);
        }
        finally { gate.Release(); }
    }

    /// <summary>One background pass: rebuild companies marked dirty, plus any whose promotions started, ended or depend on stock.</summary>
    public async Task SweepAsync(bool checkAll, CancellationToken ct)
    {
        var todo = _dirty.Keys.ToHashSet();
        if (checkAll)
        {
            await using var anon = await uows.BeginAnonymousAsync(ct);
            foreach (var id in await anon.Promos.ActiveCompanyIdsAsync(ct))
            {
                await using var scoped = await uows.BeginSystemAsync(id, ct);
                if (await scoped.Promos.NeedsRefreshAsync(id, ct)) todo.Add(id);
            }
        }
        foreach (var id in todo) await RebuildAsync(id, ct);
    }
}

public sealed record VariantBody(long? ColorId, long? SizeGroupId, string? SizeCode, string? Barcode, long PurchasePrice, long SalePrice, string? Status, long? RowVer = null);

public sealed record ProductBody(string? Code, string? Name, string? NameUr, long DepartmentId, long CategoryId, long? BrandId, long? SeasonId, long? GenderId,
                                 long? ProductTypeId, long? UnitId, long? LocationId, decimal? Weight, string? Description, decimal TaxRate, string? Status,
                                 List<VariantBody>? Variants, long? RowVer = null);

public sealed record PriceBody(long PurchasePrice, long SalePrice, string? Reason);
public sealed record StorePriceBody(long StoreId, long? SalePrice);
public sealed record PriceBandBody(string? Name, long MinPrice, long? MaxPrice, int SortOrder, long? RowVer = null);

public sealed record BulkPriceBody(long[]? VariantIds, long[]? ProductIds, long? CategoryId, long? BrandId, string? Field, string? Mode, decimal Value,
                                   int RoundStep = 1, string? Reason = null, bool Preview = true);

public sealed class CatalogService(IUnitOfWorkFactory uows, PriceRefresher prices)
{
    // ------------------------------------------------------------------------------------------ products

    public async Task<Page<Row>> ProductsAsync(UserContext u, ProductFilter f, PageQuery q, CancellationToken ct)
    {
        if (f.StoreId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Catalog.ProductsAsync(f, q, ct);
    }

    public async Task<Row> ProductAsync(UserContext u, long id, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        var p = await uow.Catalog.ProductAsync(id, ct) ?? throw new NotFoundException("Product", id);
        p["variants"] = await uow.Catalog.VariantsAsync(id, ct);
        return p;
    }

    public async Task<long> CreateProductAsync(UserContext u, ProductBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        var input = Validate(b);
        var variants = b.Variants ?? [];
        var c = new Check();
        for (var i = 0; i < variants.Count; i++) ValidateVariant(c, variants[i], $"variants[{i}]");
        c.Require(variants.Select(v => (v.ColorId, (v.SizeCode ?? "").Trim().ToUpperInvariant())).Distinct().Count() == variants.Count, "variants", "The same colour and size appears twice.");
        c.ThrowIfAny();

        await using var uow = await uows.BeginAsync(u, ct);
        var code = StoreService_Blank(b.Code)?.ToUpperInvariant();
        var id = await uow.Catalog.InsertProductAsync(u.CompanyId, input, code, ct);
        var finalCode = code ?? $"P{id}";
        foreach (var v in variants) await AddVariantCoreAsync(u, uow, id, finalCode, v, null, ct);
        await uow.CommitAsync(ct);
        if (variants.Count > 0) prices.MarkDirty(u.CompanyId);
        return id;
    }

    public async Task UpdateProductAsync(UserContext u, long id, ProductBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        var input = Validate(b);
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Catalog.ProductAsync(id, ct) is null) throw new NotFoundException("Product", id);
        if (!await uow.Catalog.UpdateProductAsync(id, input with { Code = StoreService_Blank(b.Code)?.ToUpperInvariant() }, b.RowVer, ct))
            throw new ConflictException("This article was changed by someone else. Reload and try again.", "stale");
        await uow.CommitAsync(ct);
        prices.MarkDirty(u.CompanyId);   // category or status may change which promotions apply
    }

    public async Task DeleteProductAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Catalog.ProductAsync(id, ct) is null) throw new NotFoundException("Product", id);
        // Stock of other stores may be invisible to this user; the owner's view decides. A system scope would be needed for certainty, so refuse on what is visible
        // and let the database foreign keys protect history (the article is only hidden, never physically removed).
        if (await uow.Catalog.ProductStockAsync(id, ct) > 0) throw new BusinessRuleException("This article still has stock. Adjust or transfer the stock out first.", "has_stock");
        await uow.Catalog.SoftDeleteProductAsync(id, u.UserId, ct);
        await uow.CommitAsync(ct);
        prices.MarkDirty(u.CompanyId);
    }

    // ------------------------------------------------------------------------------------------ variants

    public async Task<long> AddVariantAsync(UserContext u, long productId, VariantBody v, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        var c = new Check(); ValidateVariant(c, v, "variant"); c.ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        var p = await uow.Catalog.ProductAsync(productId, ct) ?? throw new NotFoundException("Product", productId);
        var id = await AddVariantCoreAsync(u, uow, productId, p.Get<string>("code")!, v, null, ct);
        await uow.CommitAsync(ct);
        prices.MarkDirty(u.CompanyId);
        return id;
    }

    public async Task UpdateVariantAsync(UserContext u, long id, VariantBody v, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        new Check().NotBlank(v.Barcode, "barcode").Require(v.Status is null or "ACTIVE" or "INACTIVE", "status", "Status must be ACTIVE or INACTIVE.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Catalog.VariantAsync(id, ct) is null) throw new NotFoundException("Variant", id);
        if (!await uow.Catalog.UpdateVariantAsync(id, v.Barcode!.Trim().ToUpperInvariant(), v.Status ?? "ACTIVE", v.RowVer, ct))
            throw new ConflictException("This size was changed by someone else. Reload and try again.", "stale");
        await uow.CommitAsync(ct);
    }

    public async Task DeleteVariantAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Catalog.VariantAsync(id, ct) is null) throw new NotFoundException("Variant", id);
        if (await uow.Catalog.VariantStockAsync(id, ct) > 0) throw new BusinessRuleException("This size still has stock.", "has_stock");
        await uow.Catalog.SoftDeleteVariantAsync(id, u.UserId, ct);
        await uow.CommitAsync(ct);
        prices.MarkDirty(u.CompanyId);
    }

    private static async Task<long> AddVariantCoreAsync(UserContext u, IUnitOfWork uow, long productId, string productCode, VariantBody v, Guid? batch, CancellationToken ct)
    {
        var size = v.SizeCode!.Trim().ToUpperInvariant();
        var barcode = StoreService_Blank(v.Barcode)?.ToUpperInvariant() ?? DefaultBarcode(productCode, v.ColorId, size);
        var id = await uow.Catalog.InsertVariantAsync(u.CompanyId, productId, new VariantInput(v.ColorId, v.SizeGroupId, size, barcode, v.Status ?? "ACTIVE"), ct);
        await uow.Catalog.UpsertPricesAsync(u.CompanyId, id, v.PurchasePrice, v.SalePrice, ct);
        await uow.Catalog.InsertPriceEventAsync(u.CompanyId, id, 0, "PURCHASE", null, v.PurchasePrice, batch, "New size", u.UserId, ct);
        await uow.Catalog.InsertPriceEventAsync(u.CompanyId, id, 0, "SALE", null, v.SalePrice, batch, "New size", u.UserId, ct);
        return id;
    }

    /// <summary>Default scannable barcode (Code 39 characters only): article code, colour id when there is one, size.</summary>
    public static string DefaultBarcode(string productCode, long? colorId, string size)
        => Regex.Replace($"{productCode}{(colorId is { } c ? $"C{c}" : "")}S{size}", @"[^A-Z0-9\-\.]", "", RegexOptions.IgnoreCase).ToUpperInvariant();

    // ------------------------------------------------------------------------------------------ prices

    public async Task<Row> PricesAsync(UserContext u, long variantId, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Catalog.VariantAsync(variantId, ct) is null) throw new NotFoundException("Variant", variantId);
        var p = await uow.Catalog.PriceAsync(variantId, ct) ?? new Row { ["variantId"] = variantId, ["purchasePrice"] = 0L, ["salePrice"] = 0L };
        p["storeOverrides"] = await uow.Catalog.StoreOverridesAsync(variantId, ct);
        return p;
    }

    public async Task SetPricesAsync(UserContext u, long variantId, PriceBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        new Check().Require(b.PurchasePrice >= 0, "purchasePrice", "Price cannot be negative.").Require(b.SalePrice >= 0, "salePrice", "Price cannot be negative.").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Catalog.VariantAsync(variantId, ct) is null) throw new NotFoundException("Variant", variantId);
        await ApplyPriceAsync(u, uow, variantId, b.PurchasePrice, b.SalePrice, null, b.Reason, ct);
        await uow.CommitAsync(ct);
        await prices.RebuildAsync(u.CompanyId, ct);
    }

    public async Task SetStorePriceAsync(UserContext u, long variantId, StorePriceBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit); u.RequireStore(b.StoreId);
        if (b.StoreId <= 0) throw new ValidationException("storeId", "Choose a selling store.");
        await using var uow = await uows.BeginAsync(u, ct);
        if (await uow.Catalog.VariantAsync(variantId, ct) is null) throw new NotFoundException("Variant", variantId);
        var old = await uow.Catalog.StoreOverrideAsync(b.StoreId, variantId, ct);
        if (b.SalePrice is null)
        {
            await uow.Catalog.DeleteStoreOverrideAsync(b.StoreId, variantId, ct);
        }
        else
        {
            if (b.SalePrice < 0) throw new ValidationException("salePrice", "Price cannot be negative.");
            await uow.Catalog.UpsertStoreOverrideAsync(u.CompanyId, b.StoreId, variantId, b.SalePrice.Value, u.UserId, ct);
            await uow.Catalog.InsertPriceEventAsync(u.CompanyId, variantId, b.StoreId, "SALE", old, b.SalePrice.Value, null, "Store price", u.UserId, ct);
        }
        await uow.CommitAsync(ct);
        await prices.RebuildAsync(u.CompanyId, ct);
    }

    public async Task<List<Row>> PriceHistoryAsync(UserContext u, long? variantId, Guid? batch, int limit, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Catalog.PriceEventsAsync(variantId, batch, limit <= 0 ? 200 : limit, ct);
    }

    private static async Task ApplyPriceAsync(UserContext u, IUnitOfWork uow, long variantId, long purchase, long sale, Guid? batch, string? reason, CancellationToken ct)
    {
        var cur = await uow.Catalog.PriceAsync(variantId, ct);
        var oldP = cur?.Get<long>("purchasePrice"); var oldS = cur?.Get<long>("salePrice");
        await uow.Catalog.UpsertPricesAsync(u.CompanyId, variantId, purchase, sale, ct);
        if (oldP != purchase) await uow.Catalog.InsertPriceEventAsync(u.CompanyId, variantId, 0, "PURCHASE", oldP, purchase, batch, reason, u.UserId, ct);
        if (oldS != sale) await uow.Catalog.InsertPriceEventAsync(u.CompanyId, variantId, 0, "SALE", oldS, sale, batch, reason, u.UserId, ct);
    }

    /// <summary>Change many prices at once, with a preview first. Every change shares one batch id so the whole batch can be reversed.</summary>
    public async Task<Row> BulkPricesAsync(UserContext u, BulkPriceBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        var c = new Check()
            .Require(b.Field is "SALE" or "PURCHASE", "field", "Field must be SALE or PURCHASE.")
            .Require(b.Mode is "SET" or "PERCENT" or "AMOUNT", "mode", "Mode must be SET, PERCENT or AMOUNT.")
            .Require(b.RoundStep >= 1, "roundStep", "Rounding step must be at least 1.")
            .Require(b.Mode != "SET" || b.Value >= 0, "value", "Price cannot be negative.")
            .Require((b.VariantIds?.Length ?? 0) + (b.ProductIds?.Length ?? 0) > 0 || b.CategoryId is not null || b.BrandId is not null, "target", "Choose sizes, articles, a category or a brand.");
        c.ThrowIfAny();

        await using var uow = await uows.BeginAsync(u, ct);
        var company = await uow.Stores.GetCompanyAsync(u.CompanyId, ct);
        var mode = company?.Get<string>("roundingMode") ?? "FLOOR";
        var rows = await uow.Catalog.TargetVariantsAsync(b.VariantIds ?? [], b.ProductIds ?? [], b.CategoryId, b.BrandId, ct);
        var preview = new List<Row>();
        foreach (var r in rows)
        {
            var old = r.Get<long>(b.Field == "SALE" ? "salePrice" : "purchasePrice");
            var raw = b.Mode switch { "SET" => b.Value, "PERCENT" => old * (1 + b.Value / 100m), _ => old + b.Value };
            var step = b.RoundStep;
            var nw = step == 1 ? Money.Round(raw, mode) : (long)(Math.Round(raw / step, 0, MidpointRounding.AwayFromZero) * step);
            if (nw < 0) nw = 0;
            preview.Add(new Row { ["variantId"] = r["variantId"], ["productCode"] = r["productCode"], ["productName"] = r["productName"], ["sizeCode"] = r["sizeCode"], ["oldPrice"] = old, ["newPrice"] = nw });
        }
        var changed = preview.Where(p => p.Get<long>("oldPrice") != p.Get<long>("newPrice")).ToList();
        if (b.Preview) return new Row { ["applied"] = false, ["count"] = changed.Count, ["rows"] = preview };

        var batch = Guid.NewGuid();
        foreach (var p in changed)
        {
            var vid = p.Get<long>("variantId");
            var cur = rows.First(x => x.Get<long>("variantId") == vid);
            var pur = b.Field == "PURCHASE" ? p.Get<long>("newPrice") : cur.Get<long>("purchasePrice");
            var sal = b.Field == "SALE" ? p.Get<long>("newPrice") : cur.Get<long>("salePrice");
            await ApplyPriceAsync(u, uow, vid, pur, sal, batch, b.Reason ?? "Bulk price change", ct);
        }
        await uow.CommitAsync(ct);
        if (changed.Count > 0) await prices.RebuildAsync(u.CompanyId, ct);
        return new Row { ["applied"] = true, ["count"] = changed.Count, ["batchId"] = batch, ["rows"] = changed };
    }

    /// <summary>Puts every price of a bulk batch back. A price someone changed again after the batch is left alone and reported.</summary>
    public async Task<Row> ReverseBatchAsync(UserContext u, Guid batch, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        await using var uow = await uows.BeginAsync(u, ct);
        var events = await uow.Catalog.PriceEventsAsync(null, batch, 1000, ct);
        if (events.Count == 0) throw new NotFoundException("Price batch", batch);
        var back = Guid.NewGuid(); var restored = 0; var skipped = new List<long>();
        foreach (var e in events.Where(e => e.Get<long?>("oldPrice") is not null))
        {
            var vid = e.Get<long>("variantId");
            var cur = await uow.Catalog.PriceAsync(vid, ct);
            var isSale = e.Get<string>("priceType") == "SALE";
            var now = cur?.Get<long>(isSale ? "salePrice" : "purchasePrice");
            if (now != e.Get<long>("newPrice")) { skipped.Add(vid); continue; }
            await ApplyPriceAsync(u, uow, vid, isSale ? cur!.Get<long>("purchasePrice") : e.Get<long>("oldPrice"), isSale ? e.Get<long>("oldPrice") : cur!.Get<long>("salePrice"), back, $"Reversal of batch {batch}", ct);
            restored++;
        }
        await uow.CommitAsync(ct);
        if (restored > 0) await prices.RebuildAsync(u.CompanyId, ct);
        return new Row { ["restored"] = restored, ["skippedVariantIds"] = skipped, ["batchId"] = back };
    }

    // ------------------------------------------------------------------------------------------ counter lookup

    public async Task<Row> LookupAsync(UserContext u, string barcode, long? storeId, CancellationToken ct)
    {
        var store = storeId ?? u.StoreId;
        u.RequireStore(store);
        new Check().NotBlank(barcode, "barcode").ThrowIfAny();
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Catalog.LookupAsync(barcode.Trim(), store, ct) ?? throw new NotFoundException("Barcode", barcode);
    }

    public async Task<List<Row>> SearchAsync(UserContext u, string q, long? storeId, int limit, CancellationToken ct)
    {
        var store = storeId ?? u.StoreId;
        u.RequireStore(store);
        if (string.IsNullOrWhiteSpace(q)) return [];
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Catalog.SearchVariantsAsync(q.Trim(), store, limit <= 0 ? 20 : limit, ct);
    }

    // ------------------------------------------------------------------------------------------ price bands

    public async Task<List<Row>> BandsAsync(UserContext u, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Catalog.PriceBandsAsync(ct);
    }

    public async Task<long> CreateBandAsync(UserContext u, PriceBandBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit); ValidateBand(b);
        await using var uow = await uows.BeginAsync(u, ct);
        var id = await uow.Catalog.InsertPriceBandAsync(u.CompanyId, b.Name!.Trim(), b.MinPrice, b.MaxPrice, b.SortOrder, ct);
        await uow.CommitAsync(ct);
        return id;
    }

    public async Task UpdateBandAsync(UserContext u, long id, PriceBandBody b, CancellationToken ct)
    {
        u.Require(P.MasterEdit); ValidateBand(b);
        await using var uow = await uows.BeginAsync(u, ct);
        if (!await uow.Catalog.UpdatePriceBandAsync(id, b.Name!.Trim(), b.MinPrice, b.MaxPrice, b.SortOrder, b.RowVer, ct))
            throw new ConflictException("This price band was changed by someone else, or no longer exists.", "stale");
        await uow.CommitAsync(ct);
    }

    public async Task DeleteBandAsync(UserContext u, long id, CancellationToken ct)
    {
        u.Require(P.MasterEdit);
        await using var uow = await uows.BeginAsync(u, ct);
        await uow.Catalog.DeletePriceBandAsync(id, u.UserId, ct);
        await uow.CommitAsync(ct);
    }

    // ------------------------------------------------------------------------------------------ helpers

    private static string? StoreService_Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ProductInput Validate(ProductBody b)
    {
        new Check().NotBlank(b.Name, "name").MaxLength(b.Name, 200, "name").MaxLength(b.Code, 40, "code")
            .Require(b.DepartmentId > 0, "departmentId", "Department is required.").Require(b.CategoryId > 0, "categoryId", "Category is required.")
            .Require(b.TaxRate is >= 0 and <= 100, "taxRate", "Tax rate must be between 0 and 100.")
            .Require(b.Weight is null or >= 0, "weight", "Weight cannot be negative.")
            .Require(b.Status is null or "ACTIVE" or "INACTIVE", "status", "Status must be ACTIVE or INACTIVE.").ThrowIfAny();
        return new ProductInput(StoreService_Blank(b.Code), b.Name!.Trim(), StoreService_Blank(b.NameUr), b.DepartmentId, b.CategoryId, b.BrandId, b.SeasonId, b.GenderId,
            b.ProductTypeId, b.UnitId, b.LocationId, b.Weight, StoreService_Blank(b.Description), b.TaxRate, b.Status ?? "ACTIVE");
    }

    private static void ValidateVariant(Check c, VariantBody v, string path)
        => c.NotBlank(v.SizeCode, $"{path}.sizeCode", "Size").Require(v.PurchasePrice >= 0, $"{path}.purchasePrice", "Price cannot be negative.")
            .Require(v.SalePrice >= 0, $"{path}.salePrice", "Price cannot be negative.")
            .Require(v.Barcode is null || Regex.IsMatch(v.Barcode, @"^[A-Za-z0-9\-\. $/+%]{1,40}$"), $"{path}.barcode", "Barcode may use letters, digits and - . space $ / + % (Code 39).")
            .Require(v.Status is null or "ACTIVE" or "INACTIVE", $"{path}.status", "Status must be ACTIVE or INACTIVE.");

    private static void ValidateBand(PriceBandBody b)
        => new Check().NotBlank(b.Name, "name").Require(b.MinPrice >= 0, "minPrice", "Minimum cannot be negative.")
            .Require(b.MaxPrice is null || b.MaxPrice >= b.MinPrice, "maxPrice", "Maximum must not be below the minimum.").ThrowIfAny();
}

public sealed class StockService(IUnitOfWorkFactory uows)
{
    public async Task<Page<Row>> BalancesAsync(UserContext u, long? storeId, long? productId, long? categoryId, string? search, bool onlyPositive, PageQuery q, CancellationToken ct)
    {
        if (storeId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Stock.BalancesAsync(storeId, productId, categoryId, search, onlyPositive, q, ct);
    }

    public async Task<List<Row>> BySizeAsync(UserContext u, long productId, CancellationToken ct)
    {
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Stock.BySizeAsync(productId, ct);
    }

    public async Task<Page<Row>> LedgerAsync(UserContext u, long? variantId, long? storeId, DateOnly? from, DateOnly? to, string? docType, PageQuery q, CancellationToken ct)
    {
        if (storeId is { } s) u.RequireStore(s);
        await using var uow = await uows.BeginAsync(u, ct);
        return await uow.Stock.LedgerAsync(variantId, storeId, from, to, docType, q, ct);
    }
}
