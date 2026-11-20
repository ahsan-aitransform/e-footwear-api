using System.Net;
using System.Text.Json;
using EFootwear.Api.Tests.Support;
using Xunit;

namespace EFootwear.Api.Tests;

[Collection("api")]
public sealed class CatalogTests(ApiFixture fx)
{
    private static string U(string p) => p + Guid.NewGuid().ToString("N")[..6];

    private sealed record Seed(long DepartmentId, long CategoryId, long ColorId, long SizeGroupId);

    private static async Task<long> Id(Session s, string url, object body)
    {
        var (st, b) = await s.PostAsync(url, body);
        Assert.True(st == HttpStatusCode.Created, $"{url} -> {st} {b}");
        return b.GetProperty("id").GetInt64();
    }

    private async Task<Seed> SeedMastersAsync(Session o)
    {
        var dep = await Id(o, "/api/v1/masters/departments", new { name = U("Dept") });
        var color = await Id(o, "/api/v1/masters/colors", new { name = U("Black") });
        var group = await Id(o, "/api/v1/masters/size-groups", new { name = U("Adult") });
        return new Seed(dep, 0, color, group);
    }

    private async Task<(Seed s, long cat)> MastersWithCategoryAsync(Session o)
    {
        var s = await SeedMastersAsync(o);
        var cat = await Id(o, "/api/v1/masters/categories", new { name = U("Sandals"), departmentId = s.DepartmentId });
        return (s, cat);
    }

    private async Task<(long productId, long[] variantIds, string[] barcodes)> NewProductAsync(Session o, Seed s, long cat, long sale = 2000, long purchase = 1200, string? code = null, string[]? sizes = null)
    {
        sizes ??= ["7", "8", "9"];
        var (st, b) = await o.PostAsync("/api/v1/catalog/products", new
        {
            code, name = U("Sandal"), departmentId = s.DepartmentId, categoryId = cat,
            variants = sizes.Select(z => new { colorId = s.ColorId, sizeGroupId = s.SizeGroupId, sizeCode = z, purchasePrice = purchase, salePrice = sale }).ToArray(),
        });
        Assert.True(st == HttpStatusCode.Created, $"{st} {b}");
        var id = b.GetProperty("id").GetInt64();
        var p = await o.GetJsonAsync($"/api/v1/catalog/products/{id}");
        var vs = p.GetProperty("variants").EnumerateArray().ToList();
        return (id, vs.Select(v => v.GetProperty("id").GetInt64()).ToArray(), vs.Select(v => v.GetProperty("barcode").GetString()!).ToArray());
    }

    private Task Stock(long variant, long store, int qty, int cost = 1200)
        => fx.ExecAsync("select inv.post_stock_move(@c, @s, @v, @q, 'OPENING', 0, @cost, current_date)", ("c", fx.Acme.CompanyId), ("s", store), ("v", variant), ("q", (decimal)qty), ("cost", (decimal)cost));

    private static long Price(JsonElement lookup, string name) => lookup.GetProperty(name).GetInt64();

    [Fact]
    public async Task Product_with_sizes_gets_scannable_barcodes_codes_prices_and_history()
    {
        var o = await fx.OwnerAsync();
        var (s, cat) = await MastersWithCategoryAsync(o);
        var (pid, vids, barcodes) = await NewProductAsync(o, s, cat);

        var p = await o.GetJsonAsync($"/api/v1/catalog/products/{pid}");
        Assert.StartsWith("P", p.GetProperty("code").GetString());
        Assert.Equal(3, p.GetProperty("variantCount").GetInt32());
        Assert.Equal(2000, p.GetProperty("minPrice").GetInt64());
        Assert.All(barcodes, b => Assert.Matches("^[A-Z0-9.-]+$", b));
        Assert.Equal(3, barcodes.Distinct().Count());

        var hist = await o.GetJsonAsync($"/api/v1/catalog/price-history?variantId={vids[0]}");
        Assert.Equal(2, hist.GetArrayLength());   // purchase + sale, first entry

        var found = await o.GetJsonAsync($"/api/v1/catalog/products?search={barcodes[1]}");
        Assert.Equal(pid, found.GetProperty("items")[0].GetProperty("id").GetInt64());

        // duplicate code, duplicate barcode, bad price
        var code = p.GetProperty("code").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await o.PostAsync("/api/v1/catalog/products", new { code, name = "Dup", departmentId = s.DepartmentId, categoryId = cat })).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await o.PostAsync($"/api/v1/catalog/products/{pid}/variants", new { colorId = s.ColorId, sizeGroupId = s.SizeGroupId, sizeCode = "10", barcode = barcodes[0], salePrice = 10 })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync($"/api/v1/catalog/products/{pid}/variants", new { sizeCode = "10", salePrice = -5 })).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await o.PostAsync($"/api/v1/catalog/products/{pid}/variants", new { colorId = s.ColorId, sizeGroupId = s.SizeGroupId, sizeCode = "7", salePrice = 1 })).Status);   // same colour+size
    }

    [Fact]
    public async Task Price_changes_are_logged_store_prices_win_at_the_counter_and_lookup_reads_stock()
    {
        var o = await fx.OwnerAsync();
        var (s, cat) = await MastersWithCategoryAsync(o);
        var (_, vids, barcodes) = await NewProductAsync(o, s, cat);
        var store = fx.Acme.FirstStoreId;
        await Stock(vids[0], store, 5);

        var look = await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0].ToLowerInvariant()}");   // case-insensitive scan
        Assert.Equal("VARIANT", look.GetProperty("kind").GetString());
        Assert.Equal(2000, Price(look, "salePrice"));
        Assert.Equal(5, look.GetProperty("stockQty").GetDecimal());

        Assert.Equal(HttpStatusCode.NoContent, (await o.PutAsync($"/api/v1/catalog/variants/{vids[0]}/prices", new { purchasePrice = 1300, salePrice = 2200, reason = "Season" })).Status);
        Assert.Equal(2200, Price(await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}"), "salePrice"));
        Assert.Equal(HttpStatusCode.NoContent, (await o.PutAsync($"/api/v1/catalog/variants/{vids[0]}/store-price", new { storeId = store, salePrice = 2100 })).Status);
        Assert.Equal(2100, Price(await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}"), "salePrice"));
        await o.PutAsync($"/api/v1/catalog/variants/{vids[0]}/store-price", new { storeId = store });   // clear
        Assert.Equal(2200, Price(await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}"), "salePrice"));

        var hist = await o.GetJsonAsync($"/api/v1/catalog/price-history?variantId={vids[0]}");
        var sale = hist.EnumerateArray().Where(e => e.GetProperty("priceType").GetString() == "SALE").Select(e => e.GetProperty("newPrice").GetInt64()).ToList();
        Assert.Contains(2200, sale); Assert.Contains(2100, sale);

        Assert.Equal(HttpStatusCode.NotFound, (await o.SendAsync(HttpMethod.Get, "/api/v1/catalog/lookup?barcode=NOPE")).Status);
    }

    [Fact]
    public async Task Bulk_price_change_previews_applies_in_one_batch_and_reverses_what_is_still_unchanged()
    {
        var o = await fx.OwnerAsync();
        var (s, cat) = await MastersWithCategoryAsync(o);
        var (pid, vids, _) = await NewProductAsync(o, s, cat, sale: 1000);

        var prev = (await o.PostAsync("/api/v1/catalog/prices/bulk", new { productIds = new[] { pid }, field = "SALE", mode = "PERCENT", value = 10, preview = true })).Body;
        Assert.False(prev.GetProperty("applied").GetBoolean());
        Assert.Equal(3, prev.GetProperty("count").GetInt32());
        Assert.Equal(1100, prev.GetProperty("rows")[0].GetProperty("newPrice").GetInt64());
        Assert.Equal(1000, (await o.GetJsonAsync($"/api/v1/catalog/variants/{vids[0]}/prices")).GetProperty("salePrice").GetInt64());   // preview changed nothing

        var applied = (await o.PostAsync("/api/v1/catalog/prices/bulk", new { productIds = new[] { pid }, field = "SALE", mode = "PERCENT", value = 10, preview = false, reason = "Eid" })).Body;
        var batch = applied.GetProperty("batchId").GetGuid();
        Assert.Equal(1100, (await o.GetJsonAsync($"/api/v1/catalog/variants/{vids[2]}/prices")).GetProperty("salePrice").GetInt64());
        Assert.Equal(3, (await o.GetJsonAsync($"/api/v1/catalog/price-history?batchId={batch}")).GetArrayLength());   // one SALE event per size; purchase prices did not change
    }

    [Fact]
    public async Task Bulk_reversal_restores_prices_but_leaves_ones_changed_since()
    {
        var o = await fx.OwnerAsync();
        var (s, cat) = await MastersWithCategoryAsync(o);
        var (pid, vids, _) = await NewProductAsync(o, s, cat, sale: 1000);
        var batch = (await o.PostAsync("/api/v1/catalog/prices/bulk", new { productIds = new[] { pid }, field = "SALE", mode = "AMOUNT", value = 50, preview = false })).Body.GetProperty("batchId").GetGuid();
        await o.PutAsync($"/api/v1/catalog/variants/{vids[1]}/prices", new { purchasePrice = 1200, salePrice = 1234 });   // changed again afterwards

        var rev = (await o.PostAsync($"/api/v1/catalog/prices/batches/{batch}/reverse")).Body;
        Assert.Equal(2, rev.GetProperty("restored").GetInt32());
        Assert.Equal(vids[1], rev.GetProperty("skippedVariantIds")[0].GetInt64());
        Assert.Equal(1000, (await o.GetJsonAsync($"/api/v1/catalog/variants/{vids[0]}/prices")).GetProperty("salePrice").GetInt64());
        Assert.Equal(1234, (await o.GetJsonAsync($"/api/v1/catalog/variants/{vids[1]}/prices")).GetProperty("salePrice").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync("/api/v1/catalog/prices/bulk", new { field = "SALE", mode = "SET", value = 1 })).Status);   // no target
    }

    [Fact]
    public async Task Promotions_change_the_counter_price_conflicts_hold_at_list_until_resolved()
    {
        var o = await fx.OwnerAsync();
        var (s, cat) = await MastersWithCategoryAsync(o);
        var (pid, vids, barcodes) = await NewProductAsync(o, s, cat, sale: 2000);
        await Stock(vids[0], fx.Acme.FirstStoreId, 3);
        var at = (await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}")).GetProperty("salePrice").GetInt64();
        Assert.Equal(2000, at);

        var start = DateTimeOffset.UtcNow.AddMinutes(-5);
        var r1 = await Id(o, "/api/v1/promotions", new { name = U("10% off"), discountType = "PERCENT", discountValue = 10, targetType = "PRODUCT", startsAt = start, targets = new[] { new { productId = pid } }, publish = true });
        Assert.Equal(1800, (await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}")).GetProperty("salePrice").GetInt64());

        // second overlapping rule -> conflict: list price until an admin picks
        var r2 = await Id(o, "/api/v1/promotions", new { name = U("Flat 300"), discountType = "AMOUNT", discountValue = 300, targetType = "PRODUCT", startsAt = start, targets = new[] { new { productId = pid } }, publish = true });
        var look = await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}");
        Assert.Equal(2000, look.GetProperty("salePrice").GetInt64());
        Assert.True(look.GetProperty("isConflict").GetBoolean());
        var conflicts = await o.GetJsonAsync("/api/v1/promotions/conflicts");
        var mine = conflicts.EnumerateArray().First(c => c.GetProperty("variantId").GetInt64() == vids[0]);
        Assert.Equal(2, mine.GetProperty("candidates").GetArrayLength());

        // a rule not among the candidates is refused, a real one resolves
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync($"/api/v1/promotions/conflicts/{mine.GetProperty("id").GetInt64()}/resolve", new { ruleId = 999999 })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await o.PostAsync($"/api/v1/promotions/conflicts/{mine.GetProperty("id").GetInt64()}/resolve", new { ruleId = r2 })).Status);
        Assert.Equal(1700, (await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}")).GetProperty("salePrice").GetInt64());

        // cancelling a rule changes the answer immediately
        Assert.Equal(HttpStatusCode.NoContent, (await o.PostAsync($"/api/v1/promotions/{r2}/cancel")).Status);
        Assert.Equal(1800, (await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}")).GetProperty("salePrice").GetInt64());
        await o.PostAsync($"/api/v1/promotions/{r1}/cancel");
        Assert.Equal(2000, (await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}")).GetProperty("salePrice").GetInt64());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await o.PostAsync($"/api/v1/promotions/{r1}/publish")).Status);   // cancelled cannot reopen
    }

    [Fact]
    public async Task Promotion_validation_stock_window_and_store_scope()
    {
        var o = await fx.OwnerAsync();
        var (s, cat) = await MastersWithCategoryAsync(o);
        var (pid, vids, barcodes) = await NewProductAsync(o, s, cat, sale: 1000);
        var start = DateTimeOffset.UtcNow.AddMinutes(-1);

        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync("/api/v1/promotions", new { name = "x", discountType = "PERCENT", discountValue = 150, targetType = "ALL", startsAt = start })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync("/api/v1/promotions", new { name = "x", discountType = "PERCENT", discountValue = 10, targetType = "PRODUCT", startsAt = start })).Status);   // targets missing
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync("/api/v1/promotions", new { name = "x", discountType = "PERCENT", discountValue = 10, targetType = "ALL", startsAt = start, endsAt = start.AddHours(-1) })).Status);

        // "clear the last pieces": only when stock is at most 2
        await Stock(vids[0], fx.Acme.FirstStoreId, 5);
        await Id(o, "/api/v1/promotions", new { name = U("Last pieces"), discountType = "FIXED_PRICE", discountValue = 700, targetType = "VARIANT", stockMax = 2, startsAt = start, targets = new[] { new { variantId = vids[0] } }, publish = true });
        Assert.Equal(1000, (await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}")).GetProperty("salePrice").GetInt64());   // 5 in stock: no discount
        await fx.ExecAsync("select inv.post_stock_move(@c, @s, @v, -4, 'ADJUSTMENT', 0, 0, current_date)", ("c", fx.Acme.CompanyId), ("s", fx.Acme.FirstStoreId), ("v", vids[0]));
        // stock-level rules follow stock; the background worker (or this manual refresh) re-evaluates them
        Assert.Equal(HttpStatusCode.NoContent, (await o.PostAsync("/api/v1/promotions/refresh")).Status);
        Assert.Equal(700, (await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}")).GetProperty("salePrice").GetInt64());   // 1 left: last-pieces price

        // a store-scoped rule only prices that store
        var second = await Id(o, "/api/v1/stores", new { code = U("S").ToUpperInvariant(), name = "Other store" });
        var posChannel = (await o.GetJsonAsync("/api/v1/promotions/channels")).EnumerateArray().First(c => c.GetProperty("code").GetString() == "POS").GetProperty("id").GetInt64();
        await Id(o, "/api/v1/promotions", new { name = U("Branch only"), discountType = "PERCENT", discountValue = 50, targetType = "VARIANT", startsAt = start, targets = new[] { new { variantId = vids[2] } }, storeIds = new[] { second }, channelIds = new[] { posChannel }, publish = true });
        Assert.Equal(1000, (await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[2]}&storeId={fx.Acme.FirstStoreId}")).GetProperty("salePrice").GetInt64());
        Assert.Equal(500, (await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[2]}&storeId={second}")).GetProperty("salePrice").GetInt64());
        // a store-scoped rule with no POS channel is refused by the database rule
        var bad = await o.PostAsync("/api/v1/promotions", new { name = U("NoChannel"), discountType = "PERCENT", discountValue = 5, targetType = "VARIANT", startsAt = start, targets = new[] { new { variantId = vids[1] } }, storeIds = new[] { second } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.Status);
    }

    [Fact]
    public async Task Deals_are_found_by_their_own_barcode_and_cannot_reuse_an_article_barcode()
    {
        var o = await fx.OwnerAsync();
        var (s, cat) = await MastersWithCategoryAsync(o);
        var (_, vids, barcodes) = await NewProductAsync(o, s, cat, sale: 1000);
        var bc = U("DEAL").ToUpperInvariant();
        var items = new[] { new { variantId = vids[0], qty = 1, netUnitPrice = 800 }, new { variantId = vids[1], qty = 1, netUnitPrice = 700 } };
        var deal = await Id(o, "/api/v1/promotions/deals", new { name = "Pair", barcode = bc, bundlePrice = 1500, startsAt = DateTimeOffset.UtcNow.AddMinutes(-1), items, publish = true });
        var look = await o.GetJsonAsync($"/api/v1/catalog/lookup?barcode={bc}");
        Assert.Equal("BUNDLE", look.GetProperty("kind").GetString());
        Assert.Equal(1500, look.GetProperty("bundlePrice").GetInt64());
        Assert.Equal(2, look.GetProperty("items").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync("/api/v1/promotions/deals", new { name = "Bad sum", barcode = U("D"), bundlePrice = 999, startsAt = DateTimeOffset.UtcNow, items })).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await o.PostAsync("/api/v1/promotions/deals", new { name = "Clash", barcode = barcodes[0], bundlePrice = 1500, startsAt = DateTimeOffset.UtcNow, items })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await o.PostAsync($"/api/v1/promotions/deals/{deal}/cancel")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await o.SendAsync(HttpMethod.Get, $"/api/v1/catalog/lookup?barcode={bc}")).Status);
    }

    [Fact]
    public async Task Price_bands_may_not_overlap()
    {
        var o = await fx.OwnerAsync();
        var lo = 900_000 + Random.Shared.Next(0, 5000) * 10;
        Assert.Equal(HttpStatusCode.Created, (await o.PostAsync("/api/v1/catalog/price-bands", new { name = U("A"), minPrice = lo, maxPrice = lo + 999 })).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await o.PostAsync("/api/v1/catalog/price-bands", new { name = U("B"), minPrice = lo + 500, maxPrice = lo + 1500 })).Status);
        Assert.Equal(HttpStatusCode.Created, (await o.PostAsync("/api/v1/catalog/price-bands", new { name = U("C"), minPrice = lo + 1000, maxPrice = lo + 1999 })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsync("/api/v1/catalog/price-bands", new { name = U("D"), minPrice = 50, maxPrice = 10 })).Status);
    }

    [Fact]
    public async Task Stock_is_visible_per_store_a_cashier_only_sees_their_own_and_other_companies_see_nothing()
    {
        var o = await fx.OwnerAsync();
        var (s, cat) = await MastersWithCategoryAsync(o);
        var (pid, vids, barcodes) = await NewProductAsync(o, s, cat);
        var second = await Id(o, "/api/v1/stores", new { code = U("S").ToUpperInvariant(), name = "Second" });
        await Stock(vids[0], fx.Acme.FirstStoreId, 4);
        await Stock(vids[0], second, 6);

        Assert.Equal(10, (await o.GetJsonAsync($"/api/v1/catalog/products/{pid}")).GetProperty("stockQty").GetDecimal());
        var bal = await o.GetJsonAsync($"/api/v1/stock?productId={pid}");
        Assert.Equal(2, bal.GetProperty("total").GetInt32());
        Assert.Equal(4, (await o.GetJsonAsync($"/api/v1/stock?productId={pid}&storeId={fx.Acme.FirstStoreId}")).GetProperty("items")[0].GetProperty("qtyOnHand").GetDecimal());
        Assert.Equal(2, (await o.GetJsonAsync($"/api/v1/stock/ledger?variantId={vids[0]}")).GetProperty("total").GetInt32());

        // a cashier of the first store sees that store only; the other store's quantity is invisible, even by direct request
        var name = U("c");
        await Id(o, "/api/v1/users", new { userName = name, fullName = name, role = "CASHIER_SALESMAN", password = "Start-Pass-1", storeIds = new[] { fx.Acme.FirstStoreId }, permissions = new[] { "SALE_CREATE" } });
        var c = await fx.LoginAsync("Acme Footwear", name, "Start-Pass-1");
        await c.PostAsync("/api/v1/auth/change-password", new { currentPassword = "Start-Pass-1", newPassword = "Next-Pass-22" });
        c = await fx.LoginAsync("Acme Footwear", name, "Next-Pass-22");
        var seen = await c.GetJsonAsync($"/api/v1/stock?productId={pid}");
        Assert.Equal(1, seen.GetProperty("total").GetInt32());
        Assert.Equal(4, seen.GetProperty("items")[0].GetProperty("qtyOnHand").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(HttpMethod.Get, $"/api/v1/stock?storeId={second}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(HttpMethod.Get, $"/api/v1/catalog/lookup?barcode={barcodes[0]}&storeId={second}")).Status);
        Assert.Equal(4, (await c.GetJsonAsync($"/api/v1/catalog/lookup?barcode={barcodes[0]}")).GetProperty("stockQty").GetDecimal());
        // the cashier can scan but not edit prices or promotions
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsync($"/api/v1/catalog/variants/{vids[0]}/prices", new { purchasePrice = 1, salePrice = 1 })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/v1/promotions", new { name = "x", discountType = "PERCENT", discountValue = 5, targetType = "ALL", startsAt = DateTimeOffset.UtcNow })).Status);

        // stock blocks deletion
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await o.DeleteAsync($"/api/v1/catalog/products/{pid}")).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await o.DeleteAsync($"/api/v1/catalog/variants/{vids[0]}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await o.DeleteAsync($"/api/v1/catalog/variants/{vids[2]}")).Status);   // no stock

        // another company sees nothing of this
        var g = await fx.LoginAsync("Globex Shoes", "owner", ApiFixture.OwnerPassword);
        Assert.Equal(HttpStatusCode.NotFound, (await g.SendAsync(HttpMethod.Get, $"/api/v1/catalog/products/{pid}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await g.SendAsync(HttpMethod.Get, $"/api/v1/catalog/lookup?barcode={barcodes[0]}")).Status);
        Assert.Equal(0, (await g.GetJsonAsync($"/api/v1/stock?productId={pid}")).GetProperty("total").GetInt32());
    }
}
