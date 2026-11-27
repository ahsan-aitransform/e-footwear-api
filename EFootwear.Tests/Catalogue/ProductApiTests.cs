using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Catalogue.CatalogueTestHelpers;

namespace EFootwear.Tests.Catalogue;

/// <summary>New Product and View Products (docx 4.1, 4.2): creation, codes, barcodes, editing, deletion, listing, permissions, tenant isolation.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class ProductApiTests(ApiTestEnvironment environment)
{
    private async Task<(OwnerCompanyScenario Scenario, CatalogueLookups Lookups)> NewCompanyAsync()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        return (scenario, await CreateLookupsAsync(scenario.OwnerClient));
    }

    private static object UpdateBody(CatalogueLookups lookups, JsonElement product, string name, IEnumerable<object> variants) =>
        ProductBody(lookups, name, [.. variants], rowVersion: product.GetProperty("rowVersion").GetInt64());

    private static object ExistingVariant((Guid VariantId, string SizeCode, string Barcode, long PurchasePrice, long SalePrice, long RowVersion) variant,
                                          long? salePrice = null) =>
        Variant(variant.SizeCode, variant.PurchasePrice, salePrice ?? variant.SalePrice, variant.Barcode, variantId: variant.VariantId, rowVersion: variant.RowVersion);

    [Fact]
    public async Task A_product_is_created_with_an_allocated_code_default_barcodes_and_initial_price_events()
    {
        var (scenario, lookups) = await NewCompanyAsync();

        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Classic Sandal", Variant("27", 550, 999), Variant("28", 550, 999));
        var code = created.GetProperty("code").GetString()!;
        var product = await GetProductAsync(scenario.OwnerClient, created.GetProperty("productId").GetGuid());

        Assert.Equal(code, product.GetProperty("code").GetString());
        Assert.Equal([code + "S27", code + "S28"], ReadVariants(product).Select(variant => variant.Barcode).ToArray());
        var history = await PriceHistoryAsync(scenario.OwnerClient, created.GetProperty("productId").GetGuid());
        var initial = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal("INITIAL_PRICE", initial.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("oldSalePrice").ValueKind);
        Assert.Equal(550, initial.GetProperty("newPurchasePriceLowest").GetInt64());
        Assert.Equal(999, initial.GetProperty("newSalePriceHighest").GetInt64());
    }

    [Fact]
    public async Task Product_codes_run_on_in_a_company_and_the_preview_shows_the_code_the_next_product_gets()
    {
        var (scenario, lookups) = await NewCompanyAsync();

        var preview = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.NextCode}"));
        var first = await CreateProductAsync(scenario.OwnerClient, lookups, "First", Variant("40", 100, 200));
        var second = await CreateProductAsync(scenario.OwnerClient, lookups, "Second", Variant("40", 100, 200));

        Assert.Equal(preview.GetProperty("code").GetString(), first.GetProperty("code").GetString());
        Assert.Equal(long.Parse(first.GetProperty("code").GetString()!) + 1, long.Parse(second.GetProperty("code").GetString()!));
    }

    [Fact]
    public async Task A_barcode_used_by_another_product_is_refused_and_the_failed_product_is_not_left_behind()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        await CreateProductAsync(scenario.OwnerClient, lookups, "First", Variant("40", 100, 200, barcode: "SHARED-1"));

        var duplicate = await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "Second", [Variant("41", 100, 200, barcode: "SHARED-1")]));

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Conflict, duplicate);
        var rows = await ListVariantsAsync(scenario.OwnerClient);
        Assert.Equal(1, rows.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task The_same_size_twice_no_size_a_negative_price_or_a_category_of_another_department_are_validation_errors()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var otherDepartment = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, TestScenarioBuilder.Url(ApiRoutes.MasterData.Departments), new { name = "Bags" })).GetProperty("id").GetGuid();
        var foreignCategory = await CreateCategoryAsync(scenario.OwnerClient, otherDepartment, "Totes");

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest,
            await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "Twice", [Variant("40", 1, 2), Variant("40", 1, 2)])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest,
            await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "Empty", [])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest,
            await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "Negative", [Variant("40", 1, -2)])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest,
            await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "   ", [Variant("40", 1, 2)])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest,
            await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "Wrong category", [Variant("40", 1, 2)], categoryId: foreignCategory)));
    }

    [Fact]
    public async Task A_size_in_two_colours_needs_its_own_barcode_for_each_colour()
    {
        var (scenario, lookups) = await NewCompanyAsync();

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl,
            ProductBody(lookups, "Two colours", [Variant("40", 1, 2, colorId: lookups.BlackColorId), Variant("40", 1, 2, colorId: lookups.BrownColorId)])));

        var created = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, ProductsUrl, ProductBody(lookups, "Two colours",
            [Variant("40", 1, 2, barcode: "BLK-40", colorId: lookups.BlackColorId), Variant("40", 1, 2, barcode: "BRN-40", colorId: lookups.BrownColorId)]));
        Assert.Equal(2, ReadVariants(await GetProductAsync(scenario.OwnerClient, created.GetProperty("productId").GetGuid())).Count);
    }

    [Fact]
    public async Task Editing_a_product_changes_the_name_adds_a_size_and_keeps_the_existing_barcodes()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("27", 550, 999));
        var productId = created.GetProperty("productId").GetGuid();
        var product = await GetProductAsync(scenario.OwnerClient, productId);
        var existing = ReadVariants(product).Single();

        var response = await scenario.OwnerClient.PutAsJsonAsync(ProductUrl(productId),
            UpdateBody(lookups, product, "Sandal Deluxe", [ExistingVariant(existing), Variant("28", 560, 1099)]));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NoContent, response);

        var after = await GetProductAsync(scenario.OwnerClient, productId);
        Assert.Equal("Sandal Deluxe", after.GetProperty("name").GetString());
        var variants = ReadVariants(after);
        Assert.Equal(2, variants.Count);
        Assert.Equal(existing.Barcode, variants.Single(variant => variant.SizeCode == "27").Barcode);
        Assert.Equal(created.GetProperty("code").GetString() + "S28", variants.Single(variant => variant.SizeCode == "28").Barcode);
    }

    [Fact]
    public async Task Changing_a_sale_price_writes_an_event_with_the_old_and_the_new_price()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("27", 550, 999));
        var productId = created.GetProperty("productId").GetGuid();
        var product = await GetProductAsync(scenario.OwnerClient, productId);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.NoContent, await scenario.OwnerClient.PutAsJsonAsync(ProductUrl(productId),
            UpdateBody(lookups, product, "Sandal", [ExistingVariant(ReadVariants(product).Single(), salePrice: 1199)])));

        Assert.Equal(1199, ReadVariants(await GetProductAsync(scenario.OwnerClient, productId)).Single().SalePrice);
        var history = await PriceHistoryAsync(scenario.OwnerClient, productId);
        var change = history.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("reason").GetString() == "PRODUCT_EDIT");
        Assert.Equal(JsonValueKind.Null, change.GetProperty("newPurchasePriceLowest").ValueKind);
        Assert.Equal(999, change.GetProperty("oldSalePrice").GetInt64());
        Assert.Equal(1199, change.GetProperty("newSalePriceLowest").GetInt64());
    }

    [Fact]
    public async Task A_user_without_the_selling_price_right_can_edit_the_name_but_not_a_sale_price()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("27", 550, 999));
        var productId = created.GetProperty("productId").GetGuid();
        var product = await GetProductAsync(scenario.OwnerClient, productId);
        var variant = ReadVariants(product).Single();
        var editor = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.NewProduct.Edit);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await editor.PutAsJsonAsync(ProductUrl(productId),
            UpdateBody(lookups, product, "Sandal", [ExistingVariant(variant, salePrice: 1500)])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NoContent, await editor.PutAsJsonAsync(ProductUrl(productId),
            UpdateBody(lookups, product, "Sandal Renamed", [ExistingVariant(variant)])));
        Assert.Equal(999, ReadVariants(await GetProductAsync(scenario.OwnerClient, productId)).Single().SalePrice);
    }

    [Fact]
    public async Task A_stale_row_version_is_refused_with_409_so_two_editors_cannot_overwrite_each_other()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("27", 550, 999));
        var productId = created.GetProperty("productId").GetGuid();
        var product = await GetProductAsync(scenario.OwnerClient, productId);
        var variant = ExistingVariant(ReadVariants(product).Single());

        TestScenarioBuilder.AssertStatus(HttpStatusCode.NoContent, await scenario.OwnerClient.PutAsJsonAsync(ProductUrl(productId), UpdateBody(lookups, product, "First edit", [variant])));
        var second = await scenario.OwnerClient.PutAsJsonAsync(ProductUrl(productId), UpdateBody(lookups, product, "Second edit", [variant]));

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Conflict, second);
        Assert.Equal("CONCURRENT_UPDATE", (await TestScenarioBuilder.ReadProblemAsync(second)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_barcode_change_in_the_product_screen_is_kept_in_the_barcode_history()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("27", 550, 999, barcode: "OLD-27"));
        var productId = created.GetProperty("productId").GetGuid();
        var product = await GetProductAsync(scenario.OwnerClient, productId);
        var variant = ReadVariants(product).Single();

        TestScenarioBuilder.AssertStatus(HttpStatusCode.NoContent, await scenario.OwnerClient.PutAsJsonAsync(ProductUrl(productId),
            UpdateBody(lookups, product, "Sandal", [Variant("27", 550, 999, barcode: "NEW-27", variantId: variant.VariantId, rowVersion: variant.RowVersion)])));

        var history = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{BarcodesUrl}/{ApiRoutes.Barcodes.History}?variantId={variant.VariantId}"));
        var entry = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal("OLD-27", entry.GetProperty("oldBarcode").GetString());
        Assert.Equal("NEW-27", entry.GetProperty("newBarcode").GetString());
    }

    [Fact]
    public async Task A_size_left_out_of_the_saved_list_is_removed_unless_stock_or_documents_still_use_it()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("27", 550, 999), Variant("28", 550, 999));
        var productId = created.GetProperty("productId").GetGuid();
        var product = await GetProductAsync(scenario.OwnerClient, productId);
        var variants = ReadVariants(product);
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, variants.Single(variant => variant.SizeCode == "28").VariantId, 5);

        var blocked = await scenario.OwnerClient.PutAsJsonAsync(ProductUrl(productId), UpdateBody(lookups, product, "Sandal", [ExistingVariant(variants.Single(variant => variant.SizeCode == "27"))]));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Conflict, blocked);
        Assert.Equal("RECORD_IN_USE", (await TestScenarioBuilder.ReadProblemAsync(blocked)).GetProperty("errorCode").GetString());

        var allowed = await scenario.OwnerClient.PutAsJsonAsync(ProductUrl(productId), UpdateBody(lookups, product, "Sandal", [ExistingVariant(variants.Single(variant => variant.SizeCode == "28"))]));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NoContent, allowed);
        Assert.Equal(["28"], ReadVariants(await GetProductAsync(scenario.OwnerClient, productId)).Select(variant => variant.SizeCode).ToArray());
    }

    [Fact]
    public async Task A_product_with_stock_cannot_be_deleted_and_an_unused_one_can_and_its_barcode_becomes_free_again()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var used = await CreateProductAsync(scenario.OwnerClient, lookups, "Used", Variant("27", 550, 999));
        var unused = await CreateProductAsync(scenario.OwnerClient, lookups, "Unused", Variant("27", 550, 999, barcode: "REUSE-1"));
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId,
            ReadVariants(await GetProductAsync(scenario.OwnerClient, used.GetProperty("productId").GetGuid())).Single().VariantId, 3);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Conflict, await scenario.OwnerClient.DeleteAsync(ProductUrl(used.GetProperty("productId").GetGuid())));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NoContent, await scenario.OwnerClient.DeleteAsync(ProductUrl(unused.GetProperty("productId").GetGuid())));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await scenario.OwnerClient.GetAsync(ProductUrl(unused.GetProperty("productId").GetGuid())));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Created, await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "Again", [Variant("27", 1, 2, barcode: "REUSE-1")])));
    }

    [Fact]
    public async Task View_products_has_one_row_per_size_with_stock_prices_net_price_and_the_docx_margin()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("27", 550, 999), Variant("28", 550, 999));
        var variants = ReadVariants(await GetProductAsync(scenario.OwnerClient, created.GetProperty("productId").GetGuid()));
        await SeedStockAsync(environment, scenario.CompanyId, scenario.FirstStoreId, variants[0].VariantId, 4);

        var rows = await ListVariantsAsync(scenario.OwnerClient, $"storeId={scenario.FirstStoreId}");

        Assert.Equal(2, rows.GetProperty("totalCount").GetInt32());
        var size27 = rows.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("sizeCode").GetString() == "27");
        Assert.Equal(4m, size27.GetProperty("stockQuantity").GetDecimal());
        Assert.Equal(550, size27.GetProperty("purchasePrice").GetInt64());
        Assert.Equal(999, size27.GetProperty("salePrice").GetInt64());
        Assert.Equal(999, size27.GetProperty("netPrice").GetInt64());
        Assert.Equal(44.94m, size27.GetProperty("marginPercent").GetDecimal());
        Assert.Equal("Shoes", size27.GetProperty("departmentName").GetString());
        Assert.Equal("Bata", size27.GetProperty("brandName").GetString());
    }

    [Fact]
    public async Task The_list_search_finds_a_part_of_the_name_or_the_barcode()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        await CreateProductAsync(scenario.OwnerClient, lookups, "Alpha Sandal", Variant("40", 1, 2, barcode: "A-111"));
        await CreateProductAsync(scenario.OwnerClient, lookups, "Beta Shoe", Variant("40", 1, 2, barcode: "B-222"));
        string[] Names(JsonElement result) => result.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("productName").GetString()!).ToArray();
        async Task<JsonElement> Search(string text) => await ListVariantsAsync(scenario.OwnerClient, $"searchText={text}");

        Assert.Equal(["Beta Shoe"], Names(await Search("eta")));
        Assert.Equal(["Alpha Sandal"], Names(await Search("111")));
        Assert.Empty(Names(await Search("%25")));
    }

    [Fact]
    public async Task The_list_filters_by_category_brand_and_active_status()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var formal = await CreateCategoryAsync(scenario.OwnerClient, lookups.DepartmentId, "Formal");
        var other = await CreateBrandAsync(scenario.OwnerClient, "Servis");
        await CreateProductAsync(scenario.OwnerClient, lookups, "Casual Bata", Variant("40", 1, 2));
        await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "Formal Servis", [Variant("40", 1, 2)], categoryId: formal, brandId: other));
        await scenario.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "Hidden", [Variant("40", 1, 2)], status: "INACTIVE"));

        Assert.Equal(1, (await ListVariantsAsync(scenario.OwnerClient, $"categoryId={formal}")).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await ListVariantsAsync(scenario.OwnerClient, $"brandId={other}")).GetProperty("totalCount").GetInt32());
        Assert.Equal(3, (await ListVariantsAsync(scenario.OwnerClient)).GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await ListVariantsAsync(scenario.OwnerClient, "onlyActive=true")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Every_product_action_checks_its_permission_on_the_server()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("27", 550, 999));
        var productId = created.GetProperty("productId").GetGuid();
        var nobody = await scenario.SignInManagerWithPermissionsAsync();

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.Variants}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync(ProductUrl(productId)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.NextCode}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "X", [Variant("1", 1, 2)])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.DeleteAsync(ProductUrl(productId)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync($"{ProductUrl(productId)}/price-history"));
    }

    [Fact]
    public async Task Margin_and_markup_are_sent_to_the_company_owner_only()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("27", 550, 999));
        var manager = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.ViewProducts.View);

        async Task<JsonElement> FirstRowAsync(HttpClient client) =>
            (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.Variants}"))).GetProperty("items")[0];
        var forOwner = await FirstRowAsync(scenario.OwnerClient);
        var forManager = await FirstRowAsync(manager);

        Assert.Equal(44.94m, forOwner.GetProperty("marginPercent").GetDecimal());
        Assert.Equal(JsonValueKind.Number, forOwner.GetProperty("markupPercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, forManager.GetProperty("marginPercent").ValueKind);
        Assert.Equal(JsonValueKind.Null, forManager.GetProperty("markupPercent").ValueKind);
    }

    [Fact]
    public async Task A_cashier_is_not_sent_the_purchase_price_and_cannot_sort_by_it()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        await CreateProductAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 900, 999));
        await CreateProductAsync(scenario.OwnerClient, lookups, "Beta", Variant("27", 100, 999));
        var cashier = await scenario.SignInUserWithPermissionsAsync(RoleCodes.CashierSalesman, [scenario.FirstStoreId], PermissionCodes.Products.ViewProducts.View);
        var manager = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.ViewProducts.View);

        async Task<JsonElement[]> RowsAsync(HttpClient client) =>
            (await OwnerCompanyScenario.ReadJsonAsync(await client.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.Variants}?sort=purchasePrice")))
            .GetProperty("items").EnumerateArray().ToArray();
        var forCashier = await RowsAsync(cashier);
        var forManager = await RowsAsync(manager);

        Assert.All(forCashier, row => Assert.Equal(JsonValueKind.Null, row.GetProperty("purchasePrice").ValueKind));
        Assert.All(forCashier, row => Assert.Equal(JsonValueKind.Null, row.GetProperty("marginPercent").ValueKind));
        Assert.Equal(["Alpha", "Beta"], forCashier.Select(row => row.GetProperty("productName").GetString()!).ToArray()); // by name, not by price
        Assert.Equal([100L, 900L], forManager.Select(row => row.GetProperty("purchasePrice").GetInt64()).ToArray());
    }

    [Fact]
    public async Task A_person_who_may_view_products_cannot_create_or_delete_them()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("27", 550, 999));
        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.ViewProducts.View);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await viewer.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.Variants}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await viewer.PostAsJsonAsync(ProductsUrl, ProductBody(lookups, "X", [Variant("1", 1, 2)])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await viewer.DeleteAsync(ProductUrl(created.GetProperty("productId").GetGuid())));
    }

    [Fact]
    public async Task Another_company_cannot_see_find_change_or_delete_a_product()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var created = await CreateProductAsync(scenario.OwnerClient, lookups, "Secret Sandal", Variant("27", 550, 999, barcode: "SECRET-27"));
        var productId = created.GetProperty("productId").GetGuid();
        var (rival, rivalLookups) = await NewCompanyAsync();

        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await rival.OwnerClient.GetAsync(ProductUrl(productId)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await rival.OwnerClient.DeleteAsync(ProductUrl(productId)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await rival.OwnerClient.PutAsJsonAsync(ProductUrl(productId),
            ProductBody(rivalLookups, "Stolen", [Variant("27", 1, 2)], rowVersion: 1)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await rival.OwnerClient.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.PriceCheck}?barcode=SECRET-27"));
        Assert.Equal(0, (await ListVariantsAsync(rival.OwnerClient)).GetProperty("totalCount").GetInt32());
        // the same barcode is free in the other company
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Created, await rival.OwnerClient.PostAsJsonAsync(ProductsUrl, ProductBody(rivalLookups, "Mine", [Variant("27", 1, 2, barcode: "SECRET-27")])));
    }

    [Fact]
    public async Task A_store_user_cannot_ask_for_the_stock_of_a_store_they_do_not_work_in()
    {
        var (scenario, _) = await NewCompanyAsync();
        var secondStore = await scenario.CreateStoreAsync("S2");
        var manager = await scenario.SignInUserWithPermissionsAsync("STORE_MANAGER", [scenario.FirstStoreId], PermissionCodes.Products.ViewProducts.View);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await manager.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.Variants}?storeId={scenario.FirstStoreId}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await manager.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.Variants}?storeId={secondStore}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await manager.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.Variants}?storeId={Guid.Empty}"));
    }
}
