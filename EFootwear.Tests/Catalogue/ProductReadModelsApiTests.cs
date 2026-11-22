using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Catalogue.CatalogueTestHelpers;

namespace EFootwear.Tests.Catalogue;

/// <summary>Check Price (docx 4.5), Product Ledger (4.4), Products Stock with Sizes On Multiple Stores (4.14) and the Product Report (4.15).</summary>
[Collection(ApiIntegrationCollection.Name)]
public class ProductReadModelsApiTests(ApiTestEnvironment environment)
{
    private static string Url(string segment) => $"{ProductsUrl}/{segment}";

    private async Task<(OwnerCompanyScenario Scenario, CatalogueLookups Lookups)> NewCompanyAsync()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        return (scenario, await CreateLookupsAsync(scenario.OwnerClient));
    }

    private static async Task<(Guid ProductId, IReadOnlyList<(Guid VariantId, string SizeCode, string Barcode, long PurchasePrice, long SalePrice, long RowVersion)> Variants)>
        CreateAsync(HttpClient client, CatalogueLookups lookups, string name, params object[] variants)
    {
        var created = await CreateProductAsync(client, lookups, name, variants);
        var productId = created.GetProperty("productId").GetGuid();
        return (productId, ReadVariants(await GetProductAsync(client, productId)));
    }

    // ---------- Check Price ----------

    [Fact]
    public async Task Check_price_shows_stock_and_the_store_price_and_refuses_an_unknown_or_missing_barcode()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var variant = product.Variants.Single();
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, variant.VariantId, 7);

        var price = await CheckPriceAsync(scenario.OwnerClient, variant.Barcode, scenario.FirstStoreId);

        Assert.Equal(7m, price.GetProperty("stockQuantity").GetDecimal());
        Assert.Equal(999, price.GetProperty("effectiveSalePrice").GetInt64());
        Assert.Equal("Alpha", price.GetProperty("productName").GetString());
        Assert.Equal(7m, (await CheckPriceAsync(scenario.OwnerClient, variant.Barcode)).GetProperty("stockQuantity").GetDecimal()); // all stores
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.PriceCheck)}?barcode=NOPE"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.PriceCheck)}?barcode="));
    }

    // ---------- Product Ledger ----------

    [Fact]
    public async Task The_ledger_adds_up_opening_plus_in_minus_out_and_has_a_summary_and_a_detail_form()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var variant = product.Variants.Single();
        await SeedLedgerRowAsync(environment, scenario.CompanyId, scenario.FirstStoreId, variant.VariantId, new DateOnly(2026, 11, 1), "PURCHASE", 10, 10);
        await SeedLedgerRowAsync(environment, scenario.CompanyId, scenario.FirstStoreId, variant.VariantId, new DateOnly(2026, 11, 5), "SALE", -3, 7);
        await SeedLedgerRowAsync(environment, scenario.CompanyId, scenario.FirstStoreId, variant.VariantId, new DateOnly(2026, 11, 10), "SALE", -2, 5);
        var query = $"storeId={scenario.FirstStoreId}&barcode={variant.Barcode}&fromDate=2026-11-03&toDate=2026-11-30";

        var summary = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.Ledger)}?{query}&detail=false"));
        Assert.Equal(10m, summary.GetProperty("openingQuantity").GetDecimal());
        Assert.Equal(5m, summary.GetProperty("closingQuantity").GetDecimal());
        var sales = Assert.Single(summary.GetProperty("summary").EnumerateArray());
        Assert.Equal("SALE", sales.GetProperty("documentType").GetString());
        Assert.Equal(5m, sales.GetProperty("quantityOut").GetDecimal());
        Assert.Equal(2, sales.GetProperty("movementCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("lines").GetArrayLength());

        var detail = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.Ledger)}?{query}&detail=true"));
        Assert.Equal([3m, 2m], detail.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("quantityOut").GetDecimal()).ToArray());
        Assert.Equal([7m, 5m], detail.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("quantityAfter").GetDecimal()).ToArray());

        var byProduct = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(
            $"{Url(ApiRoutes.Products.Ledger)}?storeId={scenario.FirstStoreId}&productId={product.ProductId}&fromDate=2026-11-01&toDate=2026-11-30&detail=false"));
        Assert.Equal(0m, byProduct.GetProperty("openingQuantity").GetDecimal());
        Assert.Equal(5m, byProduct.GetProperty("closingQuantity").GetDecimal());
    }

    [Fact]
    public async Task The_ledger_refuses_a_bad_range_an_unknown_barcode_a_store_the_user_cannot_use_and_another_companys_product()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var secondStore = await scenario.CreateStoreAsync("S2");
        var (rival, rivalLookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var foreign = await CreateAsync(rival.OwnerClient, rivalLookups, "Theirs", Variant("40", 1, 2));
        var manager = await scenario.SignInUserWithPermissionsAsync("STORE_MANAGER", [scenario.FirstStoreId], PermissionCodes.Products.ProductLedger.View);
        var range = "fromDate=2026-11-01&toDate=2026-11-30";

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.Ledger)}?storeId={scenario.FirstStoreId}&productId={product.ProductId}&fromDate=2026-12-01&toDate=2026-11-01"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.Ledger)}?storeId={scenario.FirstStoreId}&barcode=NOPE&{range}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.Ledger)}?storeId={scenario.FirstStoreId}&{range}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await manager.GetAsync($"{Url(ApiRoutes.Products.Ledger)}?storeId={secondStore}&productId={product.ProductId}&{range}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await manager.GetAsync($"{Url(ApiRoutes.Products.Ledger)}?storeId={scenario.FirstStoreId}&productId={product.ProductId}&{range}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.Ledger)}?storeId={scenario.FirstStoreId}&productId={foreign.ProductId}&{range}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await (await scenario.SignInManagerWithPermissionsAsync()).GetAsync($"{Url(ApiRoutes.Products.Ledger)}?storeId={scenario.FirstStoreId}&productId={product.ProductId}&{range}"));
    }

    // ---------- Products Stock with Sizes On Multiple Stores ----------

    [Fact]
    public async Task Stock_on_several_stores_lists_each_size_per_store_hides_empty_stock_and_respects_the_users_stores()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var secondStore = await scenario.CreateStoreAsync("S2");
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha Runner", Variant("27", 1, 2), Variant("28", 1, 2), Variant("29", 1, 2));
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, product.Variants[0].VariantId, 4);
        await SeedStockAsync(environment, scenario.CompanyId, secondStore, product.Variants[1].VariantId, 6);
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, product.Variants[2].VariantId, 0);

        var all = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(Url(ApiRoutes.Products.StockByStore)));
        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());

        var filtered = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.StockByStore)}?searchText=runner"));
        Assert.Equal(2, filtered.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.StockByStore)}?searchText=zzz"))).GetProperty("totalCount").GetInt32());

        var manager = await scenario.SignInUserWithPermissionsAsync("STORE_MANAGER", [scenario.FirstStoreId], PermissionCodes.Products.ProductsStockWithSizesOnMultipleStores.View);
        var mine = await OwnerCompanyScenario.ReadJsonAsync(await manager.GetAsync(Url(ApiRoutes.Products.StockByStore)));
        var row = Assert.Single(mine.GetProperty("items").EnumerateArray());
        Assert.Equal(scenario.FirstStoreId, row.GetProperty("storeId").GetGuid());
        Assert.Equal(4m, row.GetProperty("stockQuantity").GetDecimal());
    }

    [Fact]
    public async Task Another_company_never_sees_the_stock_of_a_product_it_does_not_own()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var (rival, _) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 1, 2));
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, product.Variants.Single().VariantId, 4);

        var theirs = await OwnerCompanyScenario.ReadJsonAsync(await rival.OwnerClient.GetAsync(Url(ApiRoutes.Products.StockByStore)));

        Assert.Equal(0, theirs.GetProperty("totalCount").GetInt32());
    }

    // ---------- Product Report ----------

    [Fact]
    public async Task The_product_report_has_a_row_per_product_and_store_with_the_store_price_and_the_stock_of_all_sizes()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var secondStore = await scenario.CreateStoreAsync("S2");
        var other = await CreateBrandAsync(scenario.OwnerClient, "Servis");
        var alpha = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999), Variant("28", 500, 999));
        await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "Beta", [Variant("40", 100, 200)], brandId: other));
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, alpha.Variants[0].VariantId, 4);
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, alpha.Variants[1].VariantId, 3);
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{ProductPricesUrl}/{ApiRoutes.ProductPrices.OldToNew}",
            new { priceType = "SALE", storeId = (Guid?)secondStore, departmentId = (Guid?)null, categoryId = (Guid?)null, brandId = (Guid?)null, oldPrice = 999L, newPrice = 1100L, clientTransactionId = (Guid?)null });

        var report = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.Products.Report)}?brandId={lookups.BrandId}&seasonId={lookups.SeasonId}"));

        Assert.Equal(2, report.GetProperty("totalCount").GetInt32()); // Alpha in two stores; Beta has another brand
        var firstStore = report.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("storeId").GetGuid() == scenario.FirstStoreId);
        Assert.Equal(7m, firstStore.GetProperty("stockQuantity").GetDecimal());
        Assert.Equal(999, firstStore.GetProperty("salePrice").GetInt64());
        Assert.Equal(500, firstStore.GetProperty("purchasePrice").GetInt64());
        var second = report.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("storeId").GetGuid() == secondStore);
        Assert.Equal(1100, second.GetProperty("salePrice").GetInt64());
        Assert.Equal(0m, second.GetProperty("stockQuantity").GetDecimal());
    }

    [Fact]
    public async Task The_read_only_screens_each_check_their_own_permission()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 1, 2));
        var nobody = await scenario.SignInManagerWithPermissionsAsync();
        var priceOnly = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.PriceCheck.View);
        var barcode = product.Variants.Single().Barcode;

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync($"{Url(ApiRoutes.Products.PriceCheck)}?barcode={barcode}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync(Url(ApiRoutes.Products.StockByStore)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync(Url(ApiRoutes.Products.Report)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await priceOnly.GetAsync($"{Url(ApiRoutes.Products.PriceCheck)}?barcode={barcode}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await priceOnly.GetAsync(Url(ApiRoutes.Products.StockByStore)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await priceOnly.GetAsync(Url(ApiRoutes.Products.Report)));
    }

    [Fact]
    public async Task The_product_pickers_find_active_sizes_by_text_without_prices_and_need_their_own_screen_permission()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var alpha = await CreateAsync(scenario.OwnerClient, lookups, "Alpha Sandal", Variant("27", 500, 999), Variant("28", 500, 999));
        await CreateAsync(scenario.OwnerClient, lookups, "Beta Boot", Variant("40", 700, 1500));
        var ledgerUser = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.ProductLedger.View);
        var nobody = await scenario.SignInManagerWithPermissionsAsync();

        var found = await OwnerCompanyScenario.ReadJsonAsync(await ledgerUser.GetAsync($"{Url(ApiRoutes.Products.LookupForLedger)}?searchText=alpha"));

        Assert.Equal(2, found.GetProperty("totalCount").GetInt32());
        var first = found.GetProperty("items")[0];
        Assert.Equal("Alpha Sandal", first.GetProperty("productName").GetString());
        Assert.Equal(alpha.ProductId, first.GetProperty("productId").GetGuid());
        Assert.False(first.TryGetProperty("salePrice", out _) || first.TryGetProperty("purchasePrice", out _)); // a picker never shows prices
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync(Url(ApiRoutes.Products.LookupForLedger)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await ledgerUser.GetAsync(Url(ApiRoutes.Products.LookupForLabels)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await ledgerUser.GetAsync(Url(ApiRoutes.Products.LookupForDiscountsOnStores)));
    }
}
