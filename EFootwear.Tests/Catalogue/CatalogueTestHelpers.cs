using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;

namespace EFootwear.Tests.Catalogue;

/// <summary>The master data a test product needs: one value of each classification, two colours.</summary>
public sealed record CatalogueLookups(Guid DepartmentId, Guid CategoryId, Guid BrandId, Guid GenderId, Guid SeasonId, Guid UnitId, Guid BlackColorId, Guid BrownColorId);

/// <summary>Builders and database helpers shared by the catalogue integration tests.</summary>
public static class CatalogueTestHelpers
{
    public static readonly string ProductsUrl = TestScenarioBuilder.Url(ApiRoutes.Products.Root);
    public static readonly string ProductPricesUrl = TestScenarioBuilder.Url(ApiRoutes.ProductPrices.Root);
    public static readonly string ProductDiscountsUrl = TestScenarioBuilder.Url(ApiRoutes.ProductDiscounts.Root);
    public static readonly string ProductAttributesUrl = TestScenarioBuilder.Url(ApiRoutes.ProductAttributes.Root);
    public static readonly string BarcodesUrl = TestScenarioBuilder.Url(ApiRoutes.Barcodes.Root);
    public static readonly string PriceBandsUrl = TestScenarioBuilder.Url(ApiRoutes.PriceBands.Root);

    public static string ProductUrl(Guid productId) => $"{ProductsUrl}/{productId}";

    private static async Task<Guid> CreateMasterItemAsync(HttpClient client, string route, string name) =>
        (await OwnerCompanyScenario.PostJsonAsync(client, TestScenarioBuilder.Url(route), new { name })).GetProperty("id").GetGuid();

    public static async Task<CatalogueLookups> CreateLookupsAsync(HttpClient ownerClient)
    {
        var department = await CreateMasterItemAsync(ownerClient, ApiRoutes.MasterData.Departments, "Shoes");
        var category = (await OwnerCompanyScenario.PostJsonAsync(ownerClient, TestScenarioBuilder.Url(ApiRoutes.MasterData.Categories),
            new { departmentId = department, name = "Casual" })).GetProperty("id").GetGuid();
        return new CatalogueLookups(department, category,
            await CreateMasterItemAsync(ownerClient, ApiRoutes.MasterData.Brands, "Bata"),
            await CreateMasterItemAsync(ownerClient, ApiRoutes.MasterData.Genders, "Men"),
            await CreateMasterItemAsync(ownerClient, ApiRoutes.MasterData.Seasons, "Summer"),
            await CreateMasterItemAsync(ownerClient, ApiRoutes.MasterData.Units, "Pair"),
            await CreateMasterItemAsync(ownerClient, ApiRoutes.MasterData.Colors, "Black"),
            await CreateMasterItemAsync(ownerClient, ApiRoutes.MasterData.Colors, "Brown"));
    }

    public static async Task<Guid> CreateCategoryAsync(HttpClient ownerClient, Guid departmentId, string name) =>
        (await OwnerCompanyScenario.PostJsonAsync(ownerClient, TestScenarioBuilder.Url(ApiRoutes.MasterData.Categories), new { departmentId, name })).GetProperty("id").GetGuid();

    public static async Task<Guid> CreateBrandAsync(HttpClient ownerClient, string name) => await CreateMasterItemAsync(ownerClient, ApiRoutes.MasterData.Brands, name);

    public static object Variant(string sizeCode, long purchasePrice, long salePrice, string? barcode = null, Guid? colorId = null, Guid? variantId = null,
                                 long? rowVersion = null, string? status = null) =>
        new { variantId, colorId, sizeGroupId = (Guid?)null, sizeCode, barcode, purchasePrice, salePrice, rowVersion, status };

    public static object ProductBody(CatalogueLookups lookups, string name, IReadOnlyList<object> variants, string status = "ACTIVE", long? rowVersion = null,
                                     Guid? categoryId = null, Guid? brandId = null) =>
        new
        {
            name, nameUrdu = (string?)null, departmentId = lookups.DepartmentId, categoryId = categoryId ?? lookups.CategoryId, brandId = brandId ?? lookups.BrandId,
            seasonId = lookups.SeasonId, genderId = lookups.GenderId, productTypeId = (Guid?)null, unitId = lookups.UnitId, locationId = (Guid?)null,
            weight = (decimal?)null, description = (string?)null, taxRate = 0m, taxCode = (string?)null, status, variants, rowVersion,
        };

    public static async Task<JsonElement> CreateProductAsync(HttpClient client, CatalogueLookups lookups, string name, params object[] variants) =>
        await OwnerCompanyScenario.PostJsonAsync(client, ProductsUrl, ProductBody(lookups, name, variants));

    public static async Task<JsonElement> GetProductAsync(HttpClient client, Guid productId) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(ProductUrl(productId)));

    public static IReadOnlyList<(Guid VariantId, string SizeCode, string Barcode, long PurchasePrice, long SalePrice, long RowVersion)> ReadVariants(JsonElement product) =>
        product.GetProperty("variants").EnumerateArray().Select(variant => (variant.GetProperty("variantId").GetGuid(), variant.GetProperty("sizeCode").GetString()!,
            variant.GetProperty("barcode").GetString()!, variant.GetProperty("purchasePrice").GetInt64(), variant.GetProperty("salePrice").GetInt64(),
            variant.GetProperty("rowVersion").GetInt64())).ToList();

    /// <summary>Puts stock on hand into a store directly (the stock module posts it in real life; the catalogue only reads it).</summary>
    public static Task SeedStockAsync(ApiTestEnvironment environment, Guid companyId, Guid storeId, Guid variantId, decimal quantity) =>
        environment.Database.ExecuteAsOwnerAsync("""
            insert into inv.stock_balances (company_id, store_id, variant_id, qty_on_hand) values ($1, $2, $3, $4)
            on conflict (company_id, store_id, variant_id) do update set qty_on_hand = excluded.qty_on_hand
            """, companyId, storeId, variantId, quantity);

    public static Task SeedLedgerRowAsync(ApiTestEnvironment environment, Guid companyId, Guid storeId, Guid variantId, DateOnly date, string documentType,
                                          decimal quantity, decimal quantityAfter) =>
        environment.Database.ExecuteAsOwnerAsync("""
            insert into inv.stock_ledger (company_id, store_id, variant_id, txn_date, doc_type, doc_id, qty, unit_cost, qty_after)
            values ($1, $2, $3, $4, $5, $6, $7, 100, $8)
            """, companyId, storeId, variantId, date, documentType, Guid.NewGuid(), quantity, quantityAfter);

    /// <summary>The price a customer pays in a store for a barcode (Check Price).</summary>
    public static async Task<JsonElement> CheckPriceAsync(HttpClient client, string barcode, Guid? storeId = null) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync(
            $"{ProductsUrl}/{ApiRoutes.Products.PriceCheck}?barcode={Uri.EscapeDataString(barcode)}" + (storeId is null ? "" : $"&storeId={storeId}")));

    public static async Task<JsonElement> ListVariantsAsync(HttpClient client, string query = "") =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.Variants}{(query.Length == 0 ? "" : "?" + query)}"));

    public static async Task<JsonElement> PriceHistoryAsync(HttpClient client, Guid productId) =>
        await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{ProductUrl(productId)}/price-history?pageSize=200"));
}
