using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Catalogue.CatalogueTestHelpers;

namespace EFootwear.Tests.Catalogue;

/// <summary>Bulk price changes (docx 4.7, 4.8, 4.10): whole rupees, the company's rounding, store prices, price events, idempotency, permissions, isolation.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class ProductPricingApiTests(ApiTestEnvironment environment)
{
    private static string Url(string segment) => $"{ProductPricesUrl}/{segment}";

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

    private static object OldToNew(long oldPrice, long newPrice, string priceType = "SALE", Guid? storeId = null, Guid? clientTransactionId = null) =>
        new { priceType, storeId, departmentId = (Guid?)null, categoryId = (Guid?)null, brandId = (Guid?)null, oldPrice, newPrice, clientTransactionId };

    private static object Percentage(decimal? increase, decimal? decrease, Guid? storeId = null, Guid? clientTransactionId = null) =>
        new { storeId, departmentId = (Guid?)null, categoryId = (Guid?)null, brandId = (Guid?)null, increasePercent = increase, decreasePercent = decrease, clientTransactionId };

    private static async Task<long> SalePriceAsync(HttpClient client, Guid productId, string sizeCode) =>
        ReadVariants(await GetProductAsync(client, productId)).Single(variant => variant.SizeCode == sizeCode).SalePrice;

    [Fact]
    public async Task Old_to_new_previews_then_changes_only_the_sizes_at_the_old_price_and_records_events_in_one_batch()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var first = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999), Variant("28", 500, 999));
        var second = await CreateAsync(scenario.OwnerClient, lookups, "Beta", Variant("40", 700, 1299));

        var preview = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.OldToNewPreview), OldToNew(999, 1099));
        Assert.Equal(2, preview.GetProperty("totalCount").GetInt32());
        Assert.All(preview.GetProperty("items").EnumerateArray(), row =>
        {
            Assert.Equal(999, row.GetProperty("currentPrice").GetInt64());
            Assert.Equal(1099, row.GetProperty("newPrice").GetInt64());
        });
        Assert.Equal(999, await SalePriceAsync(scenario.OwnerClient, first.ProductId, "27"));

        var applied = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(999, 1099));

        Assert.Equal(2, applied.GetProperty("changedVariantCount").GetInt32());
        Assert.False(applied.GetProperty("wasAlreadyApplied").GetBoolean());
        Assert.Equal(1099, await SalePriceAsync(scenario.OwnerClient, first.ProductId, "27"));
        Assert.Equal(1099, await SalePriceAsync(scenario.OwnerClient, first.ProductId, "28"));
        Assert.Equal(1299, await SalePriceAsync(scenario.OwnerClient, second.ProductId, "40"));
        var events = (await PriceHistoryAsync(scenario.OwnerClient, first.ProductId)).GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("reason").GetString() == "BULK_OLD_TO_NEW").ToList();
        Assert.Single(events);
        Assert.All(events, item =>
        {
            Assert.Equal(999, item.GetProperty("oldSalePrice").GetInt64());
            Assert.Equal(1099, item.GetProperty("newSalePriceLowest").GetInt64());
            Assert.Equal(applied.GetProperty("batchId").GetGuid(), item.GetProperty("batchId").GetGuid());
        });
        Assert.Equal(0, (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(999, 1099))).GetProperty("changedVariantCount").GetInt32());
    }

    [Fact]
    public async Task A_retry_with_the_same_client_transaction_id_changes_nothing_twice()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var transaction = Guid.NewGuid();

        var first = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(999, 1099, clientTransactionId: transaction));
        // put the price back by another batch so a repeated change would be visible
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(1099, 999));
        var retry = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(999, 1099, clientTransactionId: transaction));

        Assert.Equal(1, first.GetProperty("changedVariantCount").GetInt32());
        Assert.True(retry.GetProperty("wasAlreadyApplied").GetBoolean());
        Assert.Equal(999, await SalePriceAsync(scenario.OwnerClient, product.ProductId, "27"));
        var batchEvents = (await PriceHistoryAsync(scenario.OwnerClient, product.ProductId)).GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("batchId").ValueKind != JsonValueKind.Null && item.GetProperty("batchId").GetGuid() == transaction);
        Assert.Equal(1, batchEvents);
    }

    [Fact]
    public async Task Percentage_changes_are_rounded_down_in_whole_rupees_and_unchanged_sizes_are_left_out()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var up = await CreateAsync(scenario.OwnerClient, lookups, "Up", Variant("27", 500, 999));
        var tiny = await CreateAsync(scenario.OwnerClient, lookups, "Tiny", Variant("27", 50, 100));

        // 999 + 10% = 1098.9 -> 1098 (FLOOR, the company default); 100 + 0.01% = 100.01 -> 100: unchanged, so not touched
        var preview = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.PercentagePreview), Percentage(10m, null));
        Assert.Equal(2, preview.GetProperty("totalCount").GetInt32());
        var small = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.PercentagePreview), Percentage(0.01m, null));
        Assert.Equal(0, small.GetProperty("totalCount").GetInt32());

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.Percentage), Percentage(10m, null));
        Assert.Equal(1098, await SalePriceAsync(scenario.OwnerClient, up.ProductId, "27"));
        Assert.Equal(110, await SalePriceAsync(scenario.OwnerClient, tiny.ProductId, "27"));

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.Percentage), Percentage(null, 33.33m));
        Assert.Equal(732, await SalePriceAsync(scenario.OwnerClient, up.ProductId, "27")); // 1098 * 66.67% = 732.0
    }

    [Fact]
    public async Task A_company_set_to_round_to_the_nearest_rupee_rounds_up_from_half()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Up", Variant("27", 500, 999));
        await environment.Database.ExecuteAsOwnerAsync("update platform.companies set rounding_mode = 'NEAREST' where id = $1", scenario.CompanyId);

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.Percentage), Percentage(10m, null));

        Assert.Equal(1099, await SalePriceAsync(scenario.OwnerClient, product.ProductId, "27")); // 1098.9 rounds to 1099
    }

    [Fact]
    public async Task A_percentage_change_needs_exactly_one_of_increase_and_decrease_within_range()
    {
        var (scenario, _) = await NewCompanyAsync();
        var url = Url(ApiRoutes.ProductPrices.Percentage);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(url, Percentage(null, null)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(url, Percentage(10m, 10m)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(url, Percentage(null, 100m)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(url, Percentage(0m, null)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(url, Percentage(1.234m, null)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(100, 100)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(-1, 100)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(1, 100, priceType: "OTHER")));
    }

    [Fact]
    public async Task A_store_price_change_makes_a_store_price_and_leaves_the_list_price_and_other_stores_alone()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var secondStore = await scenario.CreateStoreAsync("S2");
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var barcode = product.Variants.Single().Barcode;

        var applied = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(999, 1050, storeId: scenario.FirstStoreId));

        Assert.Equal(1, applied.GetProperty("changedVariantCount").GetInt32());
        Assert.Equal(999, await SalePriceAsync(scenario.OwnerClient, product.ProductId, "27"));
        var inFirst = await CheckPriceAsync(scenario.OwnerClient, barcode, scenario.FirstStoreId);
        Assert.Equal(999, inFirst.GetProperty("listPrice").GetInt64());
        Assert.Equal(1050, inFirst.GetProperty("storePrice").GetInt64());
        Assert.Equal(1050, inFirst.GetProperty("effectiveSalePrice").GetInt64());
        Assert.Equal(999, (await CheckPriceAsync(scenario.OwnerClient, barcode, secondStore)).GetProperty("effectiveSalePrice").GetInt64());
        var storeEvent = (await PriceHistoryAsync(scenario.OwnerClient, product.ProductId)).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("reason").GetString() == "BULK_OLD_TO_NEW");
        Assert.Equal(scenario.FirstStoreId, storeEvent.GetProperty("storeId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, storeEvent.GetProperty("storeName").ValueKind);
    }

    [Fact]
    public async Task Purchase_prices_belong_to_the_company_and_cannot_be_changed_for_one_store()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.OldToNew),
            OldToNew(500, 600, priceType: "PURCHASE", storeId: scenario.FirstStoreId)));
        var applied = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(500, 600, priceType: "PURCHASE"));

        Assert.Equal(1, applied.GetProperty("changedVariantCount").GetInt32());
        var variant = ReadVariants(await GetProductAsync(scenario.OwnerClient, product.ProductId)).Single();
        Assert.Equal(600, variant.PurchasePrice);
        Assert.Equal(999, variant.SalePrice);
    }

    [Fact]
    public async Task A_new_sale_price_is_set_for_every_size_of_the_selected_products_only()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var selected = await CreateAsync(scenario.OwnerClient, lookups, "Selected", Variant("27", 500, 999), Variant("28", 500, 1099));
        var other = await CreateAsync(scenario.OwnerClient, lookups, "Other", Variant("27", 500, 999));

        var applied = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.NewSalePrice),
            new { storeId = (Guid?)null, productIds = new[] { selected.ProductId }, newSalePrice = 799L, clientTransactionId = (Guid?)null });

        Assert.Equal(2, applied.GetProperty("changedVariantCount").GetInt32());
        Assert.Equal(799, await SalePriceAsync(scenario.OwnerClient, selected.ProductId, "27"));
        Assert.Equal(799, await SalePriceAsync(scenario.OwnerClient, selected.ProductId, "28"));
        Assert.Equal(999, await SalePriceAsync(scenario.OwnerClient, other.ProductId, "27"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.NewSalePrice),
            new { storeId = (Guid?)null, productIds = Array.Empty<Guid>(), newSalePrice = 1L, clientTransactionId = (Guid?)null }));
    }

    [Fact]
    public async Task The_price_history_cannot_be_changed_or_deleted_even_by_the_database_owner()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));

        await Assert.ThrowsAnyAsync<Exception>(() => environment.Database.ExecuteAsOwnerAsync(
            "update catalog.price_events set new_price = 1 where company_id = $1", scenario.CompanyId));
        await Assert.ThrowsAnyAsync<Exception>(() => environment.Database.ExecuteAsOwnerAsync(
            "delete from catalog.price_events where company_id = $1", scenario.CompanyId));
    }

    [Fact]
    public async Task The_price_history_lists_the_newest_change_first_and_pages()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(999, 1050));

        var page = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{ProductUrl(product.ProductId)}/price-history?pageSize=1"));

        Assert.Equal(2, page.GetProperty("totalCount").GetInt32()); // the first prices are one line, the change another
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.Equal("BULK_OLD_TO_NEW", page.GetProperty("items")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task The_bulk_price_screens_check_view_for_the_preview_and_edit_for_the_change()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var nobody = await scenario.SignInManagerWithPermissionsAsync();
        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.ChangePricesOldToNew.View, PermissionCodes.Products.ChangePricesByPercentage.View);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.OldToNewPreview), OldToNew(999, 1000)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.PercentagePreview), Percentage(1m, null)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await viewer.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.OldToNewPreview), OldToNew(999, 1000)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await viewer.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.PercentagePreview), Percentage(1m, null)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await viewer.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(999, 1000)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await viewer.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.Percentage), Percentage(1m, null)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await viewer.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.NewSalePrice),
            new { storeId = (Guid?)null, productIds = new[] { Guid.NewGuid() }, newSalePrice = 1L, clientTransactionId = (Guid?)null }));
    }

    [Fact]
    public async Task A_bulk_change_in_one_company_never_touches_another_companys_prices()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var (rival, rivalLookups) = await NewCompanyAsync();
        await CreateAsync(scenario.OwnerClient, lookups, "Mine", Variant("27", 500, 999));
        var theirs = await CreateAsync(rival.OwnerClient, rivalLookups, "Theirs", Variant("27", 500, 999));

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(999, 1500));

        Assert.Equal(999, await SalePriceAsync(rival.OwnerClient, theirs.ProductId, "27"));
    }

    [Fact]
    public async Task A_store_user_cannot_change_prices_of_a_store_they_do_not_work_in()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var secondStore = await scenario.CreateStoreAsync("S2");
        await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        var manager = await scenario.SignInUserWithPermissionsAsync("STORE_MANAGER", [scenario.FirstStoreId],
            PermissionCodes.Products.ChangePricesOldToNew.View, PermissionCodes.Products.ChangePricesOldToNew.Edit);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await manager.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(999, 1000, storeId: secondStore)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await manager.PostAsJsonAsync(Url(ApiRoutes.ProductPrices.OldToNew), OldToNew(999, 1000, storeId: scenario.FirstStoreId)));
    }
}
