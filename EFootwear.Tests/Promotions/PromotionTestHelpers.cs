using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Catalogue;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;

namespace EFootwear.Tests.Promotions;

/// <summary>Urls, request bodies and shop set-up shared by the promotion tests (special offers, sales, deals and conflicts).</summary>
public static class PromotionTestHelpers
{
    public static readonly string PromotionsUrl = TestScenarioBuilder.Url(ApiRoutes.Promotions.Root);

    public static string SpecialOffersUrl => $"{PromotionsUrl}/{ApiRoutes.Promotions.SpecialOffers}";
    public static string SpecialOfferUrl(Guid offerId) => $"{SpecialOffersUrl}/{offerId}";
    public static string SpecialOfferLinesUrl(Guid offerId) => $"{SpecialOfferUrl(offerId)}/lines";
    public static string SpecialOfferLineUrl(Guid offerId, Guid lineId) => $"{SpecialOfferLinesUrl(offerId)}/{lineId}";
    public static string SalesOnStoresUrl => $"{PromotionsUrl}/{ApiRoutes.Promotions.SalesOnStores}";
    public static string SaleOnStoresUrl(Guid saleId) => $"{SalesOnStoresUrl}/{saleId}";
    public static string SalesOnCategoriesUrl => $"{PromotionsUrl}/{ApiRoutes.Promotions.SalesOnCategories}";
    public static string SaleOnCategoriesUrl(Guid saleId) => $"{SalesOnCategoriesUrl}/{saleId}";
    public static string DealsUrl => $"{PromotionsUrl}/{ApiRoutes.Promotions.Deals}";
    public static string DealUrl(Guid dealId) => $"{DealsUrl}/{dealId}";
    public static string DealVariantUrl(string barcode) => $"{DealsUrl}/variant-by-barcode?barcode={Uri.EscapeDataString(barcode)}";
    public static string ConflictsUrl => $"{PromotionsUrl}/{ApiRoutes.Promotions.Conflicts}";
    public static string ResolveConflictUrl(Guid groupId) => $"{ConflictsUrl}/{groupId}/resolve";
    public static string RefreshPricesUrl => $"{PromotionsUrl}/{ApiRoutes.Promotions.RefreshPrices}";

    public static DateTimeOffset YesterdayStart => DateTimeOffset.UtcNow.AddDays(-1);
    public static DateTimeOffset NextMonthEnd => DateTimeOffset.UtcNow.AddDays(30);

    public static object RuleBody(
        string name, decimal? percent = null, IEnumerable<Guid>? storeIds = null, IEnumerable<Guid>? categoryIds = null, bool isActive = true,
        DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, long? rowVersion = null) =>
        new
        {
            name,
            discountPercent = percent,
            startsAt = startsAt ?? YesterdayStart,
            endsAt = endsAt ?? NextMonthEnd,
            isActive,
            storeIds = (storeIds ?? []).ToArray(),
            categoryIds = (categoryIds ?? []).ToArray(),
            rowVersion,
        };

    public static object DealLine(Guid variantId, decimal quantity, long netUnitPrice) => new { variantId, quantity, netUnitPrice };

    public static object DealBody(
        string name, IEnumerable<object> lines, string? barcode = null, bool isActive = true, long? rowVersion = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null) =>
        new { name, barcode, startsAt = startsAt ?? YesterdayStart, endsAt = endsAt ?? NextMonthEnd, isActive, lines = lines.ToArray(), rowVersion };

    public static Task<JsonElement> CreateSaleOnStoresAsync(HttpClient client, string name, decimal percent, params Guid[] storeIds) =>
        OwnerCompanyScenario.PostJsonAsync(client, SalesOnStoresUrl, RuleBody(name, percent, storeIds));

    public static Task<JsonElement> CreateSpecialOfferAsync(HttpClient client, string name, params Guid[] storeIds) =>
        OwnerCompanyScenario.PostJsonAsync(client, SpecialOffersUrl, RuleBody(name, null, storeIds));

    public static async Task<JsonElement> AddOfferLinesAsync(HttpClient client, Guid offerId, string? barcode, Guid? categoryId, Guid? brandId, decimal percent)
    {
        var response = await client.PostAsJsonAsync(SpecialOfferLinesUrl(offerId), new { barcode, categoryId, brandId, discountPercent = percent });
        await TestScenarioBuilder.EnsureSuccessAsync(response);
        return await OwnerCompanyScenario.ReadJsonAsync(response);
    }

    public static async Task<JsonElement> ListOfferLinesAsync(HttpClient client, Guid offerId) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(SpecialOfferLinesUrl(offerId)));

    /// <summary>The price the pricing engine gives a size in a store (what New Sale reads), through the price check of the catalogue.</summary>
    public static async Task<long> PriceAtAsync(HttpClient client, string barcode, Guid storeId) =>
        (await CatalogueTestHelpers.CheckPriceAsync(client, barcode, storeId)).GetProperty("effectiveSalePrice").GetInt64();

    public sealed record Shop(OwnerCompanyScenario Scenario, CatalogueLookups Lookups, Guid ProductId, string FirstBarcode, Guid FirstVariantId, string SecondBarcode, Guid SecondVariantId);

    /// <summary>A company with one product of two sizes (27 and 28) sold at 1 000 each, in the category Shoes -- Casual.</summary>
    public static async Task<Shop> NewShopAsync(ApiTestEnvironment environment)
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lookups = await CatalogueTestHelpers.CreateLookupsAsync(scenario.OwnerClient);
        var created = await CatalogueTestHelpers.CreateProductAsync(
            scenario.OwnerClient, lookups, "Gents Sandal", CatalogueTestHelpers.Variant("27", 500, 1_000), CatalogueTestHelpers.Variant("28", 500, 1_000));
        var productId = created.GetProperty("productId").GetGuid();
        var variants = CatalogueTestHelpers.ReadVariants(await CatalogueTestHelpers.GetProductAsync(scenario.OwnerClient, productId));
        var first = variants.Single(variant => variant.SizeCode == "27");
        var second = variants.Single(variant => variant.SizeCode == "28");
        return new Shop(scenario, lookups, productId, first.Barcode, first.VariantId, second.Barcode, second.VariantId);
    }

    /// <summary>A second product in another category, sold at 2 000.</summary>
    public static async Task<(Guid CategoryId, string Barcode, Guid VariantId)> AddProductInAnotherCategoryAsync(Shop shop)
    {
        var categoryId = (await OwnerCompanyScenario.PostJsonAsync(shop.Scenario.OwnerClient, TestScenarioBuilder.Url(ApiRoutes.MasterData.Categories),
            new { departmentId = shop.Lookups.DepartmentId, name = "Formal" })).GetProperty("id").GetGuid();
        var created = await OwnerCompanyScenario.PostJsonAsync(shop.Scenario.OwnerClient, CatalogueTestHelpers.ProductsUrl,
            CatalogueTestHelpers.ProductBody(shop.Lookups, "Formal Shoe", [CatalogueTestHelpers.Variant("42", 900, 2_000)], categoryId: categoryId));
        var productId = created.GetProperty("productId").GetGuid();
        var variant = CatalogueTestHelpers.ReadVariants(await CatalogueTestHelpers.GetProductAsync(shop.Scenario.OwnerClient, productId)).Single();
        return (categoryId, variant.Barcode, variant.VariantId);
    }
}
