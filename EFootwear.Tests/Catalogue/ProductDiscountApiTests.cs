using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Catalogue.CatalogueTestHelpers;

namespace EFootwear.Tests.Catalogue;

/// <summary>Product discounts as promotion rules (docx 4.9, 4.11, 4.12): the price the POS reads comes from the pricing engine; the list price never changes.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class ProductDiscountApiTests(ApiTestEnvironment environment)
{
    private static string Url(string segment) => $"{ProductDiscountsUrl}/{segment}";

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

    private static object StockRange(Guid storeId, decimal minimum, decimal maximum, decimal percent, Guid? clientTransactionId = null) =>
        new { storeId, brandId = (Guid?)null, minimumStock = minimum, maximumStock = maximum, discountPercent = percent, clientTransactionId };

    private static object OnStores(Guid productId, IEnumerable<Guid> storeIds, decimal percent, Guid? clientTransactionId = null) =>
        new { productId, storeIds = storeIds.ToArray(), discountPercent = percent, clientTransactionId };

    private static object Remove(Guid storeId, IEnumerable<Guid> productIds, bool restore = false) =>
        new { storeId, productIds = productIds.ToArray(), alsoRestorePreviousSalePrice = restore, clientTransactionId = (Guid?)null };

    private static async Task<long> EffectivePriceAsync(HttpClient client, string barcode, Guid storeId) =>
        (await CheckPriceAsync(client, barcode, storeId)).GetProperty("effectiveSalePrice").GetInt64();

    [Fact]
    public async Task A_stock_range_discount_covers_only_sizes_with_stock_in_the_range_in_that_store()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var secondStore = await scenario.CreateStoreAsync("S2");
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999), Variant("28", 500, 999), Variant("29", 500, 999));
        var size27 = product.Variants.Single(variant => variant.SizeCode == "27");
        var size28 = product.Variants.Single(variant => variant.SizeCode == "28");
        var size29 = product.Variants.Single(variant => variant.SizeCode == "29");
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, size27.VariantId, 1);
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, size28.VariantId, 5);
        await SeedStockAsync(environment, scenario.CompanyId, secondStore, size27.VariantId, 1);

        var preview = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.StockRangePreview), StockRange(scenario.FirstStoreId, 1, 2, 20));
        Assert.Equal(1, preview.GetProperty("totalCount").GetInt32());
        Assert.Equal("Alpha", preview.GetProperty("items")[0].GetProperty("productName").GetString());

        var applied = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.StockRange), StockRange(scenario.FirstStoreId, 1, 2, 20));

        Assert.Equal(1, applied.GetProperty("affectedProductCount").GetInt32());
        Assert.Equal(799, await EffectivePriceAsync(scenario.OwnerClient, size27.Barcode, scenario.FirstStoreId)); // 999 less 20% = 799.2 rounded down
        Assert.Equal(999, await EffectivePriceAsync(scenario.OwnerClient, size28.Barcode, scenario.FirstStoreId)); // stock 5 is outside 1..2
        Assert.Equal(999, await EffectivePriceAsync(scenario.OwnerClient, size29.Barcode, scenario.FirstStoreId)); // no stock is 0, outside 1..2
        Assert.Equal(999, await EffectivePriceAsync(scenario.OwnerClient, size27.Barcode, secondStore));           // the rule belongs to the first store only
        Assert.Equal(999, ReadVariants(await GetProductAsync(scenario.OwnerClient, product.ProductId)).Single(variant => variant.SizeCode == "27").SalePrice); // list price untouched

        var row = (await ListVariantsAsync(scenario.OwnerClient, $"storeId={scenario.FirstStoreId}")).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("sizeCode").GetString() == "27");
        Assert.Equal(200, row.GetProperty("discountAmount").GetInt64());
        Assert.Equal(799, row.GetProperty("netPrice").GetInt64());
        Assert.InRange(row.GetProperty("discountPercent").GetDecimal(), 20.01m, 20.03m);
    }

    [Fact]
    public async Task A_new_discount_replaces_the_old_one_for_the_same_product_and_store_without_a_conflict()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var barcode = product.Variants.Single().Barcode;
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, product.Variants.Single().VariantId, 1);
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.StockRange), StockRange(scenario.FirstStoreId, 1, 2, 20));

        var applied = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.OnStores), OnStores(product.ProductId, [scenario.FirstStoreId], 30));

        Assert.Equal(0, applied.GetProperty("conflictingVariantCount").GetInt32());
        var price = await CheckPriceAsync(scenario.OwnerClient, barcode, scenario.FirstStoreId);
        Assert.Equal(699, price.GetProperty("effectiveSalePrice").GetInt64()); // 999 less 30% = 699.3 rounded down
        Assert.False(price.GetProperty("isPriceInConflict").GetBoolean());
    }

    [Fact]
    public async Task A_discount_on_several_stores_sets_each_store_and_a_zero_percentage_removes_it_in_the_named_stores()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var secondStore = await scenario.CreateStoreAsync("S2");
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var barcode = product.Variants.Single().Barcode;

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.OnStores), OnStores(product.ProductId, [scenario.FirstStoreId, secondStore], 25));
        Assert.Equal(749, await EffectivePriceAsync(scenario.OwnerClient, barcode, scenario.FirstStoreId)); // 749.25 rounded down
        Assert.Equal(749, await EffectivePriceAsync(scenario.OwnerClient, barcode, secondStore));

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.OnStores), OnStores(product.ProductId, [secondStore], 0));
        Assert.Equal(749, await EffectivePriceAsync(scenario.OwnerClient, barcode, scenario.FirstStoreId));
        Assert.Equal(999, await EffectivePriceAsync(scenario.OwnerClient, barcode, secondStore));

        var rows = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{Url(ApiRoutes.ProductDiscounts.StoreRows)}?productId={product.ProductId}"));
        var first = rows.EnumerateArray().Single(row => row.GetProperty("storeId").GetGuid() == scenario.FirstStoreId);
        Assert.InRange(first.GetProperty("discountPercent").GetDecimal(), 25.0m, 25.1m);
        Assert.Equal(JsonValueKind.Null, rows.EnumerateArray().Single(row => row.GetProperty("storeId").GetGuid() == secondStore).GetProperty("discountPercent").ValueKind);
    }

    [Fact]
    public async Task Removing_discounts_puts_the_price_back_to_the_store_price()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var barcode = product.Variants.Single().Barcode;
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.OnStores), OnStores(product.ProductId, [scenario.FirstStoreId], 20));

        var removed = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.Remove), Remove(scenario.FirstStoreId, [product.ProductId]));

        Assert.Equal(0, removed.GetProperty("restoredPriceVariantCount").GetInt32());
        Assert.Equal(999, await EffectivePriceAsync(scenario.OwnerClient, barcode, scenario.FirstStoreId));
    }

    [Fact]
    public async Task Reversing_the_price_restores_the_price_before_the_last_change_only_where_that_is_clear()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var changed = await CreateAsync(scenario.OwnerClient, lookups, "Changed", Variant("27", 500, 999));
        var untouched = await CreateAsync(scenario.OwnerClient, lookups, "Untouched", Variant("27", 500, 1500));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{ProductPricesUrl}/{ApiRoutes.ProductPrices.OldToNew}",
            new { priceType = "SALE", storeId = (Guid?)null, departmentId = (Guid?)null, categoryId = (Guid?)null, brandId = (Guid?)null, oldPrice = 999L, newPrice = 1098L, clientTransactionId = (Guid?)null });

        var removed = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.Remove),
            Remove(scenario.FirstStoreId, [changed.ProductId, untouched.ProductId], restore: true));

        Assert.Equal(1, removed.GetProperty("restoredPriceVariantCount").GetInt32());
        Assert.Equal(1, removed.GetProperty("skippedRestoreVariantCount").GetInt32());
        Assert.Equal(999, ReadVariants(await GetProductAsync(scenario.OwnerClient, changed.ProductId)).Single().SalePrice);
        Assert.Equal(1500, ReadVariants(await GetProductAsync(scenario.OwnerClient, untouched.ProductId)).Single().SalePrice);
        var reversal = (await PriceHistoryAsync(scenario.OwnerClient, changed.ProductId)).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("reason").GetString() == "PRICE_REVERSAL");
        Assert.Equal(1098, reversal.GetProperty("oldPrice").GetInt64());
        Assert.Equal(999, reversal.GetProperty("newPrice").GetInt64());
    }

    [Fact]
    public async Task A_discount_that_overlaps_an_ordinary_promotion_is_a_conflict_and_the_item_stays_at_the_store_price()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var barcode = product.Variants.Single().Barcode;
        // an ordinary promotion made elsewhere: one statement, because the database checks the store/channel pairing when the statement ends
        await environment.Database.ExecuteAsOwnerAsync("""
            with r as (
                insert into promo.rules (company_id, id, name, discount_type, discount_value, target_type, starts_at, status)
                values ($1, $2, 'Eid sale', 'PERCENT', 10, 'PRODUCT', now() - interval '1 hour', 'PUBLISHED') returning id
            ), t as (
                insert into promo.rule_targets (company_id, id, rule_id, product_id) values ($1, uuidv7(), $2, $3)
            ), s as (
                insert into promo.rule_stores (company_id, rule_id, store_id) values ($1, $2, $4)
            )
            insert into promo.rule_channels (company_id, rule_id, channel_id)
            select $1, $2, id from integ.channels where company_id = $1 and kind = 'STORE_POS'
            """, scenario.CompanyId, Guid.NewGuid(), product.ProductId, scenario.FirstStoreId);

        var applied = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.OnStores), OnStores(product.ProductId, [scenario.FirstStoreId], 20));

        Assert.Equal(1, applied.GetProperty("conflictingVariantCount").GetInt32());
        var price = await CheckPriceAsync(scenario.OwnerClient, barcode, scenario.FirstStoreId);
        Assert.True(price.GetProperty("isPriceInConflict").GetBoolean());
        Assert.Equal(999, price.GetProperty("effectiveSalePrice").GetInt64());
    }

    [Fact]
    public async Task A_retry_with_the_same_client_transaction_id_does_not_create_a_second_rule()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, product.Variants.Single().VariantId, 1);
        var transaction = Guid.NewGuid();

        var first = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.StockRange), StockRange(scenario.FirstStoreId, 1, 2, 20, transaction));
        var retry = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.StockRange), StockRange(scenario.FirstStoreId, 1, 2, 20, transaction));

        Assert.False(first.GetProperty("wasAlreadyApplied").GetBoolean());
        Assert.True(retry.GetProperty("wasAlreadyApplied").GetBoolean());
        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from promo.rules where company_id = $1 and created_batch_id = $2 and created_from = 'PRODUCT_DISCOUNT'", scenario.CompanyId, transaction));
    }

    [Fact]
    public async Task The_discount_rules_made_here_carry_the_product_discount_marker()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductDiscounts.OnStores), OnStores(product.ProductId, [scenario.FirstStoreId], 15));

        Assert.Equal(1L, await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from promo.rules where company_id = $1 and created_from = 'PRODUCT_DISCOUNT' and status = 'PUBLISHED' and discount_value = 15", scenario.CompanyId));
    }

    [Fact]
    public async Task A_discount_needs_a_sensible_percentage_stock_range_and_an_existing_product_and_store()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.StockRange), StockRange(scenario.FirstStoreId, 1, 2, 0)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.StockRange), StockRange(scenario.FirstStoreId, 1, 2, 101)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.StockRange), StockRange(scenario.FirstStoreId, 5, 2, 10)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.StockRange), StockRange(scenario.FirstStoreId, 1, 2, 10))); // no product has that stock
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.StockRange), StockRange(Guid.Empty, 1, 2, 10)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.OnStores), OnStores(product.ProductId, [], 10)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.OnStores), OnStores(Guid.NewGuid(), [scenario.FirstStoreId], 10)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.Remove), Remove(scenario.FirstStoreId, [])));
    }

    [Fact]
    public async Task The_discount_screens_check_permissions_store_access_and_company_ownership()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var secondStore = await scenario.CreateStoreAsync("S2");
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var nobody = await scenario.SignInManagerWithPermissionsAsync();
        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.ChangeDiscounts.View);
        var manager = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.ProductDiscountsOnMultipleStores.View, PermissionCodes.Products.ProductDiscountsOnMultipleStores.Edit);
        var (rival, _) = await NewCompanyAsync();

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.StockRangePreview), StockRange(scenario.FirstStoreId, 1, 2, 10)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await viewer.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.StockRangePreview), StockRange(scenario.FirstStoreId, 1, 2, 10)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await viewer.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.StockRange), StockRange(scenario.FirstStoreId, 1, 2, 10)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await manager.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.Remove), Remove(scenario.FirstStoreId, [product.ProductId])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await manager.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.OnStores), OnStores(product.ProductId, [secondStore], 10)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await rival.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductDiscounts.OnStores), OnStores(product.ProductId, [rival.FirstStoreId], 10)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await rival.OwnerClient.GetAsync($"{Url(ApiRoutes.ProductDiscounts.StoreRows)}?productId={product.ProductId}"));
    }
}
