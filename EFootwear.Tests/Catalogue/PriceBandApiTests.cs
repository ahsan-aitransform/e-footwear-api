using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.MasterData;
using static EFootwear.Tests.Catalogue.CatalogueTestHelpers;

namespace EFootwear.Tests.Catalogue;

/// <summary>Price bands: named ranges of list prices that cannot overlap inside a company.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class PriceBandApiTests(ApiTestEnvironment environment)
{
    private static object Band(string name, long minimum, long? maximum, int sortOrder = 0, long? rowVersion = null) =>
        new { name, minimumPrice = minimum, maximumPrice = maximum, sortOrder, rowVersion };

    private static async Task<Guid> CreateAsync(HttpClient client, object body) =>
        (await OwnerCompanyScenario.PostJsonAsync(client, PriceBandsUrl, body)).GetProperty("id").GetGuid();

    [Fact]
    public async Task Bands_are_listed_by_sort_order_and_a_band_can_have_no_upper_limit()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        await CreateAsync(scenario.OwnerClient, Band("Premium", 5000, null, 2));
        await CreateAsync(scenario.OwnerClient, Band("Budget", 0, 999, 1));

        var list = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync(PriceBandsUrl));

        Assert.Equal(["Budget", "Premium"], list.EnumerateArray().Select(band => band.GetProperty("name").GetString()).ToArray());
        Assert.Equal(JsonValueKind.Null, list[1].GetProperty("maximumPrice").ValueKind);
    }

    [Fact]
    public async Task Overlapping_ranges_are_refused_with_409_and_a_deleted_band_frees_its_range()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var budget = await CreateAsync(scenario.OwnerClient, Band("Budget", 0, 999));

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Conflict, await scenario.OwnerClient.PostAsJsonAsync(PriceBandsUrl, Band("Overlap", 500, 1500)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NoContent, await scenario.OwnerClient.DeleteAsync($"{PriceBandsUrl}/{budget}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Created, await scenario.OwnerClient.PostAsJsonAsync(PriceBandsUrl, Band("Overlap", 500, 1500)));
    }

    [Fact]
    public async Task A_band_is_validated_updated_with_its_row_version_and_protected_against_a_stale_save()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var id = await CreateAsync(scenario.OwnerClient, Band("Budget", 0, 999));
        var band = await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{PriceBandsUrl}/{id}"));
        var rowVersion = band.GetProperty("rowVersion").GetInt64();

        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(PriceBandsUrl, Band("", 0, 10)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PostAsJsonAsync(PriceBandsUrl, Band("Bad", 500, 100)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.BadRequest, await scenario.OwnerClient.PutAsJsonAsync($"{PriceBandsUrl}/{id}", Band("No version", 0, 999)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NoContent, await scenario.OwnerClient.PutAsJsonAsync($"{PriceBandsUrl}/{id}", Band("Value", 0, 1200, 1, rowVersion)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Conflict, await scenario.OwnerClient.PutAsJsonAsync($"{PriceBandsUrl}/{id}", Band("Stale", 0, 1300, 1, rowVersion)));
        Assert.Equal("Value", (await OwnerCompanyScenario.ReadJsonAsync(await scenario.OwnerClient.GetAsync($"{PriceBandsUrl}/{id}"))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Price_bands_check_permissions_and_belong_to_one_company()
    {
        var scenario = await OwnerCompanyScenario.CreateAsync(environment);
        var rival = await OwnerCompanyScenario.CreateAsync(environment);
        var id = await CreateAsync(scenario.OwnerClient, Band("Budget", 0, 999));
        var nobody = await scenario.SignInManagerWithPermissionsAsync();
        var viewer = await scenario.SignInManagerWithPermissionsAsync(PermissionCodes.Products.PriceBands.View);

        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await nobody.GetAsync(PriceBandsUrl));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.OK, await viewer.GetAsync(PriceBandsUrl));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await viewer.PostAsJsonAsync(PriceBandsUrl, Band("X", 5000, null)));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Forbidden, await viewer.DeleteAsync($"{PriceBandsUrl}/{id}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await rival.OwnerClient.GetAsync($"{PriceBandsUrl}/{id}"));
        TestScenarioBuilder.AssertStatus(HttpStatusCode.NotFound, await rival.OwnerClient.DeleteAsync($"{PriceBandsUrl}/{id}"));
        // the same range is free in the other company
        TestScenarioBuilder.AssertStatus(HttpStatusCode.Created, await rival.OwnerClient.PostAsJsonAsync(PriceBandsUrl, Band("Budget", 0, 999)));
    }
}
