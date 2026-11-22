using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

/// <summary>A person's own preferences, and the read-only list of POS terminals.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class UserPreferencesAndTerminalsApiTests(ApiTestEnvironment environment)
{
    private static object Preferences(string? language = "ur", string? theme = "dark", Guid? defaultStoreId = null) =>
        new { languageCode = language, themeCode = theme, defaultStoreId };

    [Fact]
    public async Task Preferences_are_saved_per_person_and_validated()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var other = await scenario.CreateUserClientAsync("CASHIER_SALESMAN", [scenario.Company.FirstStoreId]);

        var initial = await (await scenario.OwnerClient.GetAsync(SettingsUrls.MyPreferences)).ReadSuccessJsonAsync();
        Assert.False(initial.GetProperty("isStored").GetBoolean());

        var saved = await (await scenario.OwnerClient.PutSettingsAsync(SettingsUrls.MyPreferences,
            Preferences(defaultStoreId: scenario.Company.FirstStoreId))).ReadSuccessJsonAsync();
        Assert.Equal("ur", saved.GetProperty("values").GetProperty("languageCode").GetString());
        Assert.Equal(scenario.Company.FirstStoreId, saved.GetProperty("values").GetProperty("defaultStoreId").GetGuid());

        // another person of the same company sees nothing of it
        var otherPreferences = await (await other.GetAsync(SettingsUrls.MyPreferences)).ReadSuccessJsonAsync();
        Assert.False(otherPreferences.GetProperty("isStored").GetBoolean());

        await (await scenario.OwnerClient.PutSettingsAsync(SettingsUrls.MyPreferences, Preferences(language: "fr")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.PutSettingsAsync(SettingsUrls.MyPreferences, Preferences(theme: "neon")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.OwnerClient.PutSettingsAsync(SettingsUrls.MyPreferences, Preferences(defaultStoreId: Guid.NewGuid())))
            .AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await scenario.PlatformClient.GetAsync(SettingsUrls.MyPreferences)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task A_default_store_must_be_one_of_the_persons_own_stores()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        var manager = await scenario.CreateUserClientAsync("STORE_MANAGER", [secondStoreId]);

        Assert.Equal(HttpStatusCode.OK, (await manager.PutSettingsAsync(SettingsUrls.MyPreferences, Preferences(defaultStoreId: secondStoreId))).StatusCode);
        await (await manager.PutSettingsAsync(SettingsUrls.MyPreferences, Preferences(defaultStoreId: scenario.Company.FirstStoreId)))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task The_terminal_list_is_read_only_permission_protected_and_limited_to_the_users_stores()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var secondStoreId = await scenario.CreateStoreAsync("S2");
        await environment.Database.ExecuteAsOwnerAsync(
            "insert into core.devices (company_id, store_id, device_uuid, label) values ($1, $2, $3, 'Counter 1')",
            scenario.Company.CompanyId, scenario.Company.FirstStoreId, Guid.NewGuid());
        await environment.Database.ExecuteAsOwnerAsync(
            "insert into core.devices (company_id, store_id, device_uuid, label) values ($1, $2, $3, 'Counter 2')",
            scenario.Company.CompanyId, secondStoreId, Guid.NewGuid());
        var manager = await scenario.CreateUserClientAsync("STORE_MANAGER", [secondStoreId], PermissionCodes.Settings.DevicesAndPosTerminals.View);
        var nobody = await scenario.CreateUserClientAsync("CASHIER_SALESMAN", [secondStoreId]);

        var ownerList = await (await scenario.OwnerClient.GetAsync(SettingsUrls.PosTerminals())).ReadSuccessJsonAsync();
        Assert.Equal(2, ownerList.GetArrayLength());
        Assert.Equal("S1", ownerList[0].GetProperty("storeCode").GetString());

        var managerList = await (await manager.GetAsync(SettingsUrls.PosTerminals())).ReadSuccessJsonAsync();
        Assert.Equal(1, managerList.GetArrayLength());
        Assert.Equal("Counter 2", managerList[0].GetProperty("label").GetString());

        await (await manager.GetAsync(SettingsUrls.PosTerminals(scenario.Company.FirstStoreId))).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await nobody.GetAsync(SettingsUrls.PosTerminals())).AssertProblemAsync(HttpStatusCode.Forbidden, "PERMISSION_DENIED");
    }
}
