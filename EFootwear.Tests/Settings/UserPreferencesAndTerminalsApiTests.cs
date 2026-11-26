using System.Net;
using System.Net.Http.Json;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Settings;

/// <summary>The read-only list of POS terminals.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class UserPreferencesAndTerminalsApiTests(ApiTestEnvironment environment)
{
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
