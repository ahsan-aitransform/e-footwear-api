using System.Net;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Catalogue.CatalogueTestHelpers;

namespace EFootwear.Tests.Catalogue;

/// <summary>A product code works wherever a barcode is looked up.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class ProductCodeSearchApiTests(ApiTestEnvironment environment)
{
    private static string PriceCheck(string code) => $"{ProductsUrl}/{ApiRoutes.Products.PriceCheck}?barcode={Uri.EscapeDataString(code)}";

    [Fact]
    public async Task A_product_code_finds_a_single_size_and_lists_the_sizes_of_a_product_with_several()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lookups = await CreateLookupsAsync(scenario.OwnerClient);
        var single = await CreateProductAsync(scenario.OwnerClient, lookups, "Socks", Variant("Free", 1, 2, barcode: "SOCK-1"));
        var several = await CreateProductAsync(scenario.OwnerClient, lookups, "Sandal", Variant("40", 1, 2), Variant("41", 1, 2));
        var singleCode = single.GetProperty("code").GetString()!;
        var severalCode = several.GetProperty("code").GetString()!;

        var found = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(PriceCheck(singleCode)));
        Assert.Equal("SOCK-1", found.GetProperty("barcode").GetString());

        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await scenario.OwnerClient.GetAsync(PriceCheck(severalCode)));
        var sizes = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.SizesByCode}?code={severalCode}"));
        Assert.Equal(["40", "41"], sizes.EnumerateArray().Select(size => size.GetProperty("sizeCode").GetString()!).ToArray());
        var none = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{ProductsUrl}/{ApiRoutes.Products.SizesByCode}?code=nothing"));
        Assert.Empty(none.EnumerateArray());
    }

    [Fact]
    public async Task A_barcode_wins_over_a_product_code_that_reads_the_same()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var lookups = await CreateLookupsAsync(scenario.OwnerClient);
        var first = await CreateProductAsync(scenario.OwnerClient, lookups, "First", Variant("40", 1, 2, barcode: "FIRST-40"));
        var firstCode = first.GetProperty("code").GetString()!;
        await CreateProductAsync(scenario.OwnerClient, lookups, "Second", Variant("41", 1, 2, barcode: firstCode));

        var found = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(PriceCheck(firstCode)));

        Assert.Equal("Second", found.GetProperty("productName").GetString());
    }
}
