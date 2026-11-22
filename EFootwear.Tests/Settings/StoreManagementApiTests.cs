using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

/// <summary>Store administration by the company, within the plan limit (ADD-05).</summary>
[Collection(ApiIntegrationCollection.Name)]
public class StoreManagementApiTests(ApiTestEnvironment environment)
{
    private static readonly string ListUrl = SettingsUrls.StoreManagement();

    private static async Task<JsonElement> FindStoreAsync(HttpClient client, string code)
    {
        var stores = await (await client.GetAsync(ListUrl)).ReadSuccessJsonAsync();
        return stores.EnumerateArray().Single(store => store.GetProperty("code").GetString() == code);
    }

    private static async Task<string> CreateAndAssignPlanAsync(SettingsScenario scenario, int maximumStores, int maximumUsers)
    {
        var code = "PLAN" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await TestScenarioBuilder.EnsureSuccessAsync(await scenario.PlatformClient.PostAsJsonAsync(
            SettingsUrls.PlatformPlans(), new { code, name = "Test plan " + code, maximumStores, maximumUsers }));
        await TestScenarioBuilder.EnsureSuccessAsync(await scenario.PlatformClient.PutAsJsonAsync(
            SettingsUrls.PlatformCompany(scenario.Company.CompanyId, ApiRoutes.PlatformCompanyAdministration.Plan), new { planCode = code }));
        return code;
    }

    [Fact]
    public async Task The_list_shows_the_head_office_first_and_a_store_can_be_created_and_edited()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);

        var stores = await (await scenario.OwnerClient.GetAsync(ListUrl)).ReadSuccessJsonAsync();
        Assert.True(stores[0].GetProperty("isHeadOffice").GetBoolean());
        Assert.Equal("S1", stores[1].GetProperty("code").GetString());

        var created = await scenario.OwnerClient.PostAsJsonAsync(ListUrl, new { code = "LAH-1", name = "Lahore Mall", address = "Mall Road", phone = "042-1" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var storeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var store = await FindStoreAsync(scenario.OwnerClient, "LAH-1");
        Assert.Equal(storeId, store.GetProperty("id").GetGuid());
        Assert.Equal("Lahore Mall", store.GetProperty("name").GetString());
        var version = store.GetProperty("rowVersion").GetInt64();

        var updateUrl = SettingsUrls.StoreManagementDetail(ApiRoutes.StoreManagement.Detail, storeId);
        var update = await scenario.OwnerClient.PutAsJsonAsync(updateUrl, new { name = "Lahore Mall Branch", address = "New address", phone = (string?)null, expectedRowVersion = version });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var changed = await FindStoreAsync(scenario.OwnerClient, "LAH-1");
        Assert.Equal("Lahore Mall Branch", changed.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, changed.GetProperty("phone").ValueKind);

        var stale = await scenario.OwnerClient.PutAsJsonAsync(updateUrl, new { name = "Again", address = (string?)null, phone = (string?)null, expectedRowVersion = version });
        await stale.AssertProblemAsync(HttpStatusCode.Conflict, "CONCURRENT_UPDATE");

        var usage = await (await scenario.OwnerClient.GetAsync(SettingsUrls.StoreManagement(ApiRoutes.StoreManagement.PlanUsage))).ReadSuccessJsonAsync();
        Assert.Equal(2, usage.GetProperty("activeStores").GetInt32());
    }

    [Fact]
    public async Task Store_codes_are_unique_and_validated()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);

        await (await scenario.OwnerClient.PostAsJsonAsync(ListUrl, new { code = "s1", name = "Same code in lower case", address = (string?)null, phone = (string?)null }))
            .AssertProblemAsync(HttpStatusCode.Conflict, "CONFLICT");
        await (await scenario.OwnerClient.PostAsJsonAsync(ListUrl, new { code = "bad code", name = "x", address = (string?)null, phone = (string?)null }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.PostAsJsonAsync(ListUrl, new { code = "OKAY", name = " ", address = (string?)null, phone = (string?)null }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task A_store_can_be_deactivated_and_reactivated_but_the_head_office_cannot()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var headOffice = (await (await scenario.OwnerClient.GetAsync(ListUrl)).ReadSuccessJsonAsync())[0].GetProperty("id").GetGuid();

        await (await scenario.OwnerClient.PostAsync(
            SettingsUrls.StoreManagementDetail(ApiRoutes.StoreManagement.Deactivate, headOffice), null))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        var deactivate = await scenario.OwnerClient.PostAsync(
            SettingsUrls.StoreManagementDetail(ApiRoutes.StoreManagement.Deactivate, scenario.Company.FirstStoreId), null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.False((await FindStoreAsync(scenario.OwnerClient, "S1")).GetProperty("isActive").GetBoolean());

        var activate = await scenario.OwnerClient.PostAsync(
            SettingsUrls.StoreManagementDetail(ApiRoutes.StoreManagement.Activate, scenario.Company.FirstStoreId), null);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);
        Assert.True((await FindStoreAsync(scenario.OwnerClient, "S1")).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task The_plan_store_limit_is_applied_with_a_clear_message_and_a_deactivated_store_frees_a_place()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        await CreateAndAssignPlanAsync(scenario, maximumStores: 1, maximumUsers: 100);

        await (await scenario.OwnerClient.PostAsJsonAsync(ListUrl, new { code = "OVER", name = "Over", address = (string?)null, phone = (string?)null }))
            .AssertProblemAsync(HttpStatusCode.Conflict, "PLAN_LIMIT_REACHED");

        await scenario.OwnerClient.PostAsync(SettingsUrls.StoreManagementDetail(ApiRoutes.StoreManagement.Deactivate, scenario.Company.FirstStoreId), null);
        var created = await scenario.OwnerClient.PostAsJsonAsync(ListUrl, new { code = "NEW1", name = "New one", address = (string?)null, phone = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await (await scenario.OwnerClient.PostAsync(
            SettingsUrls.StoreManagementDetail(ApiRoutes.StoreManagement.Activate, scenario.Company.FirstStoreId), null))
            .AssertProblemAsync(HttpStatusCode.Conflict, "PLAN_LIMIT_REACHED");

        var usage = await (await scenario.OwnerClient.GetAsync(SettingsUrls.StoreManagement(ApiRoutes.StoreManagement.PlanUsage))).ReadSuccessJsonAsync();
        Assert.Equal(1, usage.GetProperty("maximumStores").GetInt32());
        Assert.Equal(1, usage.GetProperty("activeStores").GetInt32());
    }

    [Fact]
    public async Task The_plan_user_limit_reaches_user_creation_through_the_database()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        await CreateAndAssignPlanAsync(scenario, maximumStores: 10, maximumUsers: 2);   // the owner is user 1
        await environment.Scenarios.CreateUserAsync(scenario.OwnerClient, null, "STORE_MANAGER", [scenario.Company.FirstStoreId]);

        var refused = await scenario.OwnerClient.PostAsJsonAsync(TestScenarioBuilder.Url(ApiRoutes.Users.Root), new
        {
            companyId = (Guid?)null,
            email = $"third.{Guid.NewGuid():N}@example.test",
            fullName = "Third user",
            roleCode = "STORE_MANAGER",
            storeIds = new[] { scenario.Company.FirstStoreId },
            password = TestScenarioBuilder.TemporaryPassword,
        });
        await refused.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DATABASE_RULE_VIOLATION");
    }

    [Fact]
    public async Task Store_management_needs_its_permissions_and_never_reaches_another_company()
    {
        var first = await SettingsScenario.CreateAsync(environment);
        var second = await SettingsScenario.CreateAsync(environment);
        var viewer = await first.CreateUserClientAsync("STORE_MANAGER", [first.Company.FirstStoreId], PermissionCodes.Settings.StoreManagement.View);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(ListUrl)).StatusCode);
        await (await viewer.PostAsJsonAsync(ListUrl, new { code = "NO", name = "No", address = (string?)null, phone = (string?)null }))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");
        await (await viewer.PostAsync(SettingsUrls.StoreManagementDetail(ApiRoutes.StoreManagement.Deactivate, first.Company.FirstStoreId), null))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");

        var foreign = SettingsUrls.StoreManagementDetail(ApiRoutes.StoreManagement.Detail, first.Company.FirstStoreId);
        await (await second.OwnerClient.PutAsJsonAsync(foreign, new { name = "Hijack", address = (string?)null, phone = (string?)null, expectedRowVersion = 1 }))
            .AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await second.OwnerClient.PostAsync(SettingsUrls.StoreManagementDetail(ApiRoutes.StoreManagement.Deactivate, first.Company.FirstStoreId), null))
            .AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await first.PlatformClient.GetAsync(ListUrl)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
