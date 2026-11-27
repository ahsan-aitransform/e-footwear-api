using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Catalogue.CatalogueTestHelpers;

namespace EFootwear.Tests.Catalogue;

/// <summary>Replace Barcodes (docx 4.13), the Generate Bar Codes label data, and Change Product Attributes (docx 4.6).</summary>
[Collection(ApiIntegrationCollection.Name)]
public class BarcodeAndAttributeApiTests(ApiTestEnvironment environment)
{
    private static string BarcodeUrl(string segment) => $"{BarcodesUrl}/{segment}";
    private static string AttributeUrl(string segment) => $"{ProductAttributesUrl}/{segment}";

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

    private static object Pairs(IEnumerable<(string Old, string New)> pairs, Guid? clientTransactionId = null) =>
        new { pairs = pairs.Select(pair => new { oldBarcode = pair.Old, newBarcode = pair.New }).ToArray(), reason = (string?)null, clientTransactionId };

    private static async Task<string> BarcodeOfAsync(HttpClient client, Guid productId, string sizeCode) =>
        ReadVariants(await GetProductAsync(client, productId)).Single(variant => variant.SizeCode == sizeCode).Barcode;

    // ---------- Replace Barcodes ----------

    [Fact]
    public async Task Replacing_barcodes_handles_a_swap_and_a_chain_in_one_transaction_and_keeps_a_history()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha",
            Variant("27", 1, 2, barcode: "A1"), Variant("28", 1, 2, barcode: "B1"), Variant("29", 1, 2, barcode: "C1"));

        var preview = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, BarcodeUrl(ApiRoutes.Barcodes.ReplacementPreview), Pairs([("A1", "B1"), ("B1", "A1"), ("C1", "C2")]));
        Assert.Equal(3, preview.GetProperty("validRowCount").GetInt32());

        var replaced = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, BarcodeUrl(ApiRoutes.Barcodes.Replace), Pairs([("A1", "B1"), ("B1", "A1"), ("C1", "C2")]));

        Assert.Equal(3, replaced.GetProperty("replacedCount").GetInt32());
        Assert.Equal("B1", await BarcodeOfAsync(scenario.OwnerClient, product.ProductId, "27"));
        Assert.Equal("A1", await BarcodeOfAsync(scenario.OwnerClient, product.ProductId, "28"));
        Assert.Equal("C2", await BarcodeOfAsync(scenario.OwnerClient, product.ProductId, "29"));
        var history = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{BarcodeUrl(ApiRoutes.Barcodes.History)}?barcode=C1"));
        var entry = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal("C2", entry.GetProperty("newBarcode").GetString());
        Assert.Equal(replaced.GetProperty("batchId").GetGuid(), entry.GetProperty("batchId").GetGuid());

        // capital letters do not matter, and the search box of the list searches barcodes and product names
        async Task<int> HistoryCountAsync(string query) =>
            (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{BarcodeUrl(ApiRoutes.Barcodes.History)}?{query}")))
            .GetProperty("totalCount").GetInt32();
        Assert.Equal(1, await HistoryCountAsync("barcode=c1"));
        Assert.Equal(1, await HistoryCountAsync("searchText=c2"));
        Assert.Equal(3, await HistoryCountAsync("searchText=alpha"));
        Assert.Equal(0, await HistoryCountAsync("searchText=nothing-like-this"));
    }

    [Fact]
    public async Task The_preview_names_the_problem_of_each_row_and_one_bad_row_refuses_the_whole_list()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha",
            Variant("27", 1, 2, barcode: "A1"), Variant("28", 1, 2, barcode: "A2"), Variant("29", 1, 2, barcode: "A3"), Variant("30", 1, 2, barcode: "A4"));
        await CreateAsync(scenario.OwnerClient, lookups, "Other", Variant("40", 1, 2, barcode: "D1"));

        var list = new[] { ("A1", "NEW-1"), ("UNKNOWN", "X1"), ("A2", "D1"), ("A1", "NEW-2"), ("A3", "A3"), ("", "Z"), ("A4", "has space") };
        var preview = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, BarcodeUrl(ApiRoutes.Barcodes.ReplacementPreview), Pairs(list));

        Assert.Equal(0, preview.GetProperty("validRowCount").GetInt32());
        var problems = preview.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("problem").GetString()!).ToArray();
        Assert.Contains("more than once", problems[0]);          // A1 listed twice as an old barcode
        Assert.Contains("No product has the barcode UNKNOWN", problems[1]);
        Assert.Contains("already used by another product", problems[2]);
        Assert.Contains("same as the old one", problems[4]);
        Assert.Contains("required", problems[5]);
        Assert.Contains("without spaces", problems[6]);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(BarcodeUrl(ApiRoutes.Barcodes.Replace), Pairs(list)));
        Assert.Equal("A1", await BarcodeOfAsync(scenario.OwnerClient, product.ProductId, "27"));
    }

    [Fact]
    public async Task A_valid_row_next_to_a_bad_one_is_shown_as_valid_but_nothing_is_saved_until_every_row_is_fine()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 1, 2, barcode: "A1"));

        var preview = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, BarcodeUrl(ApiRoutes.Barcodes.ReplacementPreview), Pairs([("A1", "NEW-1"), ("NOPE", "NEW-2")]));

        Assert.Equal(1, preview.GetProperty("validRowCount").GetInt32());
        Assert.Equal(1, preview.GetProperty("invalidRowCount").GetInt32());
        Assert.Equal("Alpha", preview.GetProperty("rows")[0].GetProperty("productName").GetString());
        Assert.Equal("A1", await BarcodeOfAsync(scenario.OwnerClient, product.ProductId, "27"));
    }

    [Fact]
    public async Task The_import_file_is_read_with_its_header_row_skipped_and_file_row_numbers_kept()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 1, 2, barcode: "A1"));
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("Old Barcode,New Barcode\r\nA1,Z1\r\n")), "file", "pairs.csv" },
        };

        var response = await scenario.OwnerClient.PostAsync(BarcodeUrl(ApiRoutes.Barcodes.ReplacementFile), form);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, response);
        var preview = await OwnerCompanyScenario.ReadJsonAsync(response);
        var row = Assert.Single(preview.GetProperty("rows").EnumerateArray());
        Assert.Equal("A1", row.GetProperty("oldBarcode").GetString());
        Assert.Equal(2, row.GetProperty("rowNumber").GetInt32());
        Assert.True(row.GetProperty("isValid").GetBoolean());

        using var wrongType = new MultipartFormDataContent { { new ByteArrayContent("x"u8.ToArray()), "file", "pairs.pdf" } };
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsync(BarcodeUrl(ApiRoutes.Barcodes.ReplacementFile), wrongType));
    }

    [Fact]
    public async Task A_replacement_retried_with_the_same_client_transaction_id_is_applied_once()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 1, 2, barcode: "A1"));
        var variantId = product.Variants.Single().VariantId;
        var transaction = Guid.NewGuid();

        var first = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, BarcodeUrl(ApiRoutes.Barcodes.Replace), Pairs([("A1", "Z1")], transaction));
        var retry = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, BarcodeUrl(ApiRoutes.Barcodes.Replace), Pairs([("A1", "Z1")], transaction));

        Assert.False(first.GetProperty("wasAlreadyApplied").GetBoolean());
        Assert.True(retry.GetProperty("wasAlreadyApplied").GetBoolean());
        var history = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{BarcodeUrl(ApiRoutes.Barcodes.History)}?variantId={variantId}"));
        Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task The_barcode_history_cannot_be_changed_or_deleted_even_by_the_database_owner()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 1, 2, barcode: "A1"));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, BarcodeUrl(ApiRoutes.Barcodes.Replace), Pairs([("A1", "Z1")]));

        await Assert.ThrowsAnyAsync<Exception>(() => environment.Database.ExecuteAsOwnerAsync("update catalog.barcode_history set new_barcode = 'X' where company_id = $1", scenario.CompanyId));
        await Assert.ThrowsAnyAsync<Exception>(() => environment.Database.ExecuteAsOwnerAsync("delete from catalog.barcode_history where company_id = $1", scenario.CompanyId));
    }

    [Fact]
    public async Task Barcodes_of_another_company_are_invisible_and_the_barcode_actions_check_permissions()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var (rival, _) = await NewCompanyAsync();
        await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 1, 2, barcode: "A1"));
        var nobody = await scenario.SignInManagerWithPermissionsAsync();
        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.ReplaceBarcodes.View);

        var preview = await OwnerCompanyScenario.PostJsonAsync(rival.OwnerClient, BarcodeUrl(ApiRoutes.Barcodes.ReplacementPreview), Pairs([("A1", "Z1")]));
        Assert.Equal(0, preview.GetProperty("validRowCount").GetInt32());
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.PostAsJsonAsync(BarcodeUrl(ApiRoutes.Barcodes.ReplacementPreview), Pairs([("A1", "Z1")])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await viewer.PostAsJsonAsync(BarcodeUrl(ApiRoutes.Barcodes.ReplacementPreview), Pairs([("A1", "Z1")])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await viewer.PostAsJsonAsync(BarcodeUrl(ApiRoutes.Barcodes.Replace), Pairs([("A1", "Z1")])));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync(BarcodeUrl(ApiRoutes.Barcodes.History)));
    }

    // ---------- Generate Bar Codes (label data) ----------

    [Fact]
    public async Task Label_data_has_the_size_price_and_number_of_copies_and_refuses_bad_requests()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var (rival, rivalLookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999, colorId: lookups.BlackColorId));
        var foreign = await CreateAsync(rival.OwnerClient, rivalLookups, "Theirs", Variant("40", 1, 2));
        var variantId = product.Variants.Single().VariantId;

        var labels = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, BarcodeUrl(ApiRoutes.Barcodes.Labels),
            new { items = new[] { new { variantId, copies = 3 } }, storeId = (Guid?)null });

        var label = Assert.Single(labels.EnumerateArray());
        Assert.Equal(3, label.GetProperty("copies").GetInt32());
        Assert.Equal(999, label.GetProperty("salePrice").GetInt64());
        Assert.Equal("27", label.GetProperty("sizeCode").GetString());
        Assert.Equal("Black", label.GetProperty("colorName").GetString());
        Assert.Equal("Bata", label.GetProperty("brandName").GetString());

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(BarcodeUrl(ApiRoutes.Barcodes.Labels), new { items = new[] { new { variantId, copies = 0 } }, storeId = (Guid?)null }));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(BarcodeUrl(ApiRoutes.Barcodes.Labels), new { items = Array.Empty<object>(), storeId = (Guid?)null }));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await scenario.OwnerClient.PostAsJsonAsync(BarcodeUrl(ApiRoutes.Barcodes.Labels),
            new { items = new[] { new { variantId = foreign.Variants.Single().VariantId, copies = 1 } }, storeId = (Guid?)null }));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await (await scenario.SignInManagerWithPermissionsAsync()).PostAsJsonAsync(BarcodeUrl(ApiRoutes.Barcodes.Labels),
            new { items = new[] { new { variantId, copies = 1 } }, storeId = (Guid?)null }));
    }

    [Fact]
    public async Task Label_prices_use_the_store_price_when_a_store_is_chosen()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var product = await CreateAsync(scenario.OwnerClient, lookups, "Alpha", Variant("27", 500, 999));
        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, $"{ProductPricesUrl}/{ApiRoutes.ProductPrices.OldToNew}",
            new { priceType = "SALE", storeId = (Guid?)scenario.FirstStoreId, departmentId = (Guid?)null, categoryId = (Guid?)null, brandId = (Guid?)null, oldPrice = 999L, newPrice = 1050L, clientTransactionId = (Guid?)null });

        var labels = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, BarcodeUrl(ApiRoutes.Barcodes.Labels),
            new { items = new[] { new { variantId = product.Variants.Single().VariantId, copies = 1 } }, storeId = (Guid?)scenario.FirstStoreId });

        Assert.Equal(1050, labels[0].GetProperty("salePrice").GetInt64());
    }

    // ---------- Change Product Attributes ----------

    [Fact]
    public async Task Changing_the_brand_moves_every_product_of_the_old_brand_and_the_list_shows_them_first()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var newBrand = await CreateBrandAsync(scenario.OwnerClient, "Servis");
        var one = await CreateAsync(scenario.OwnerClient, lookups, "One", Variant("40", 1, 2));
        var two = await CreateAsync(scenario.OwnerClient, lookups, "Two", Variant("40", 1, 2));

        var list = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{AttributeUrl(ApiRoutes.ProductAttributes.ProductsWithValue)}?fieldType=BRAND&oldValueId={lookups.BrandId}"));
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());

        var applied = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, AttributeUrl(ApiRoutes.ProductAttributes.Apply),
            new { fieldType = "BRAND", oldValueId = lookups.BrandId, newValueId = newBrand, clientTransactionId = (Guid?)null });

        Assert.Equal(2, applied.GetProperty("changedProductCount").GetInt32());
        Assert.Equal(newBrand, (await GetProductAsync(scenario.OwnerClient, one.ProductId)).GetProperty("brandId").GetGuid());
        Assert.Equal(newBrand, (await GetProductAsync(scenario.OwnerClient, two.ProductId)).GetProperty("brandId").GetGuid());
        var again = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, AttributeUrl(ApiRoutes.ProductAttributes.Apply),
            new { fieldType = "BRAND", oldValueId = lookups.BrandId, newValueId = newBrand, clientTransactionId = (Guid?)null });
        Assert.Equal(0, again.GetProperty("changedProductCount").GetInt32());
    }

    [Fact]
    public async Task Changing_the_category_also_moves_the_product_to_the_department_of_the_new_category()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var bags = (await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, TestScenarioBuilder.Url(ApiRoutes.MasterData.Departments), new { name = "Bags" })).GetProperty("id").GetGuid();
        var totes = await CreateCategoryAsync(scenario.OwnerClient, bags, "Totes");
        var product = await CreateAsync(scenario.OwnerClient, lookups, "One", Variant("40", 1, 2));

        await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, AttributeUrl(ApiRoutes.ProductAttributes.Apply),
            new { fieldType = "CATEGORY", oldValueId = lookups.CategoryId, newValueId = totes, clientTransactionId = (Guid?)null });

        var after = await GetProductAsync(scenario.OwnerClient, product.ProductId);
        Assert.Equal(totes, after.GetProperty("categoryId").GetGuid());
        Assert.Equal(bags, after.GetProperty("departmentId").GetGuid());
    }

    [Fact]
    public async Task Changing_a_colour_skips_a_product_that_already_has_the_size_in_the_new_colour_and_says_which()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var plain = await CreateAsync(scenario.OwnerClient, lookups, "Plain", Variant("40", 1, 2, colorId: lookups.BlackColorId));
        var both = await CreateAsync(scenario.OwnerClient, lookups, "Both", Variant("40", 1, 2, barcode: "BLK", colorId: lookups.BlackColorId), Variant("40", 1, 2, barcode: "BRN", colorId: lookups.BrownColorId));

        var applied = await OwnerCompanyScenario.PostJsonAsync(scenario.OwnerClient, AttributeUrl(ApiRoutes.ProductAttributes.Apply),
            new { fieldType = "COLOR", oldValueId = lookups.BlackColorId, newValueId = lookups.BrownColorId, clientTransactionId = (Guid?)null });

        Assert.Equal(1, applied.GetProperty("changedProductCount").GetInt32());
        var skipped = Assert.Single(applied.GetProperty("skippedProducts").EnumerateArray());
        Assert.Contains("Both", skipped.GetString());
        var plainVariant = (await GetProductAsync(scenario.OwnerClient, plain.ProductId)).GetProperty("variants")[0];
        Assert.Equal(lookups.BrownColorId, plainVariant.GetProperty("colorId").GetGuid());
    }

    [Fact]
    public async Task A_new_attribute_value_must_exist_in_the_company_differ_from_the_old_one_and_be_a_known_field()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var (rival, rivalLookups) = await NewCompanyAsync();

        Task<HttpResponseMessage> Apply(string field, Guid oldValue, Guid newValue) => scenario.OwnerClient.PostAsJsonAsync(AttributeUrl(ApiRoutes.ProductAttributes.Apply),
            new { fieldType = field, oldValueId = oldValue, newValueId = newValue, clientTransactionId = (Guid?)null });

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await Apply("BRAND", lookups.BrandId, lookups.BrandId));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await Apply("BRAND", lookups.BrandId, Guid.NewGuid()));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await Apply("BRAND", lookups.BrandId, rivalLookups.BrandId)); // another company's value is invisible
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await Apply("PASSWORD", lookups.BrandId, Guid.NewGuid()));
        Assert.NotNull(rival);
    }

    [Fact]
    public async Task The_attribute_screen_checks_view_for_the_list_and_edit_for_the_change()
    {
        var (scenario, lookups) = await NewCompanyAsync();
        var newBrand = await CreateBrandAsync(scenario.OwnerClient, "Servis");
        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.ChangeAttributes.View);
        var nobody = await scenario.SignInManagerWithPermissionsAsync();
        var query = $"{AttributeUrl(ApiRoutes.ProductAttributes.ProductsWithValue)}?fieldType=BRAND&oldValueId={lookups.BrandId}";

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync(query));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await viewer.GetAsync(query));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await viewer.PostAsJsonAsync(AttributeUrl(ApiRoutes.ProductAttributes.Apply),
            new { fieldType = "BRAND", oldValueId = lookups.BrandId, newValueId = newBrand, clientTransactionId = (Guid?)null }));
    }
}
