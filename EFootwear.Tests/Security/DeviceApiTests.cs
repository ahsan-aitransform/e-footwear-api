using System.Net;
using System.Net.Http.Json;
using EFootwear.DAL.Security;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Security;

/// <summary>POS device registration and authorization (Part 6, Part 23.7): token shown once, hashed at rest, approve and revoke, store isolation.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class DeviceApiTests(ApiTestEnvironment environment)
{
    private static async Task<(Guid DeviceId, string Token)> RegisterAsync(HttpClient client, Guid storeId, string label = "Counter 1")
    {
        var response = await client.PostAsJsonAsync(SecurityTestCompany.DevicesUrl(), new { storeId, label });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await SecurityTestCompany.ReadAsync(response);
        return (body.GetProperty("device").GetProperty("id").GetGuid(), body.GetProperty("deviceToken").GetString()!);
    }

    [Fact]
    public async Task A_registered_device_is_pending_and_its_token_is_shown_once_and_stored_only_as_a_hash()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);

        var response = await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.DevicesUrl(), new { storeId = company.FirstStoreId, label = "Counter 1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await SecurityTestCompany.ReadAsync(response);
        var token = body.GetProperty("deviceToken").GetString()!;
        var deviceId = body.GetProperty("device").GetProperty("id").GetGuid();
        Assert.Equal(64, token.Length);
        Assert.Equal("PENDING", body.GetProperty("device").GetProperty("status").GetString());

        var storedHash = await company.QueryScalarAsync<string>("select token_hash from core.devices where company_id = $1 and id = $2", company.CompanyId, deviceId);
        Assert.Equal(SessionTokenHasher.Hash(token), storedHash);
        Assert.NotEqual(token, storedHash);

        var listText = await company.OwnerClient.GetStringAsync(SecurityTestCompany.DevicesUrl());
        Assert.DoesNotContain(token, listText);
        Assert.DoesNotContain("tokenHash", listText, StringComparison.OrdinalIgnoreCase);
        var detailsText = await company.OwnerClient.GetStringAsync(SecurityTestCompany.DevicesUrl(deviceId.ToString()));
        Assert.DoesNotContain(token, detailsText);

        // the row-change log never copies the hash
        Assert.Equal(0L, await company.QueryScalarAsync<long>(
            "select count(*) from audit.activity_log where company_id = $1 and (new_values::text like '%token_hash%' or old_values::text like '%token_hash%')",
            company.CompanyId));
    }

    [Fact]
    public async Task A_device_is_authorized_then_revoked_and_each_step_is_in_the_activity_log()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (deviceId, _) = await RegisterAsync(company.OwnerClient, company.FirstStoreId);

        Assert.Equal(HttpStatusCode.NoContent, (await company.OwnerClient.PostAsync(SecurityTestCompany.DevicesUrl($"{deviceId}/authorize"), null)).StatusCode);
        var authorized = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.DevicesUrl(deviceId.ToString())));
        Assert.Equal("AUTHORIZED", authorized.GetProperty("status").GetString());
        Assert.NotNull(authorized.GetProperty("authorizedAt").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await company.OwnerClient.PostAsync(SecurityTestCompany.DevicesUrl($"{deviceId}/authorize"), null)).StatusCode);

        var revoke = await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.DevicesUrl($"{deviceId}/revoke"), new { reason = "Lost in the market" });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        var revoked = await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.DevicesUrl(deviceId.ToString())));
        Assert.Equal("REVOKED", revoked.GetProperty("status").GetString());
        Assert.Equal("Lost in the market", revoked.GetProperty("revokeReason").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.DevicesUrl($"{deviceId}/revoke"), new { })).StatusCode);

        var updates = await company.QueryScalarAsync<long>(
            "select count(*) from audit.activity_log where company_id = $1 and entity_type = 'core.devices' and entity_id = $2 and action = 'UPDATE'",
            company.CompanyId, deviceId.ToString());
        Assert.True(updates >= 2);
    }

    [Fact]
    public async Task A_new_token_replaces_the_old_one_and_returns_the_device_to_pending()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (deviceId, firstToken) = await RegisterAsync(company.OwnerClient, company.FirstStoreId);
        await company.OwnerClient.PostAsync(SecurityTestCompany.DevicesUrl($"{deviceId}/authorize"), null);

        var rotate = await company.OwnerClient.PostAsync(SecurityTestCompany.DevicesUrl($"{deviceId}/rotate-token"), null);

        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        var body = await SecurityTestCompany.ReadAsync(rotate);
        var secondToken = body.GetProperty("deviceToken").GetString()!;
        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal("PENDING", body.GetProperty("device").GetProperty("status").GetString());
        Assert.Equal(SessionTokenHasher.Hash(secondToken), await company.QueryScalarAsync<string>(
            "select token_hash from core.devices where company_id = $1 and id = $2", company.CompanyId, deviceId));
    }

    [Fact]
    public async Task Renaming_a_device_checks_the_row_version_and_the_label_rules()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (deviceId, _) = await RegisterAsync(company.OwnerClient, company.FirstStoreId);
        var rowVersion = (await SecurityTestCompany.ReadAsync(await company.OwnerClient.GetAsync(SecurityTestCompany.DevicesUrl(deviceId.ToString())))).GetProperty("rowVersion").GetInt64();

        Assert.Equal(HttpStatusCode.NoContent, (await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.DevicesUrl(deviceId.ToString()), new { label = "Counter 2", rowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.DevicesUrl(deviceId.ToString()), new { label = "Counter 3", rowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await company.OwnerClient.PutAsJsonAsync(SecurityTestCompany.DevicesUrl(deviceId.ToString()), new { label = "  ", rowVersion = rowVersion + 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await company.OwnerClient.PostAsJsonAsync(SecurityTestCompany.DevicesUrl(), new { storeId = company.FirstStoreId, label = new string('x', 200) })).StatusCode);
    }

    [Fact]
    public async Task Registering_needs_the_add_permission_and_authorizing_needs_the_approve_permission()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var (_, registrarClient) = await company.CreateSignedInUserAsync(RoleCodes.StoreManager, null,
            PermissionCodes.Settings.DevicesAndPosTerminals.View, PermissionCodes.Settings.DevicesAndPosTerminals.Add);
        var (_, noPermissionClient) = await company.CreateSignedInUserAsync(RoleCodes.CashierSalesman);

        Assert.Equal(HttpStatusCode.Forbidden, (await noPermissionClient.GetAsync(SecurityTestCompany.DevicesUrl())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPermissionClient.PostAsJsonAsync(SecurityTestCompany.DevicesUrl(), new { storeId = company.FirstStoreId, label = "x" })).StatusCode);

        var (deviceId, _) = await RegisterAsync(registrarClient, company.FirstStoreId);
        Assert.Equal(HttpStatusCode.Forbidden, (await registrarClient.PostAsync(SecurityTestCompany.DevicesUrl($"{deviceId}/authorize"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await registrarClient.PostAsJsonAsync(SecurityTestCompany.DevicesUrl($"{deviceId}/revoke"), new { })).StatusCode);
    }

    [Fact]
    public async Task A_manager_sees_and_registers_devices_only_in_its_own_stores()
    {
        var company = await SecurityTestCompany.CreateAsync(environment);
        var secondStoreId = await company.CreateStoreAsync();
        var (_, managerClient) = await company.CreateSignedInUserAsync(RoleCodes.StoreManager, null,
            PermissionCodes.Settings.DevicesAndPosTerminals.View, PermissionCodes.Settings.DevicesAndPosTerminals.Add,
            PermissionCodes.Settings.DevicesAndPosTerminals.Approve);
        var (ownDeviceId, _) = await RegisterAsync(company.OwnerClient, company.FirstStoreId, "Own store counter");
        var (otherDeviceId, _) = await RegisterAsync(company.OwnerClient, secondStoreId, "Other store counter");

        var listText = await managerClient.GetStringAsync(SecurityTestCompany.DevicesUrl());
        Assert.Contains("Own store counter", listText);
        Assert.DoesNotContain("Other store counter", listText);
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.GetAsync(SecurityTestCompany.DevicesUrl(otherDeviceId.ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.PostAsync(SecurityTestCompany.DevicesUrl($"{otherDeviceId}/authorize"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await managerClient.PostAsync(SecurityTestCompany.DevicesUrl($"{ownDeviceId}/authorize"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await managerClient.PostAsJsonAsync(SecurityTestCompany.DevicesUrl(), new { storeId = secondStoreId, label = "Sneaky" })).StatusCode);
    }

    [Fact]
    public async Task A_company_cannot_see_or_use_the_devices_of_another_company()
    {
        var companyA = await SecurityTestCompany.CreateAsync(environment);
        var companyB = await SecurityTestCompany.CreateAsync(environment);
        var (deviceId, _) = await RegisterAsync(companyA.OwnerClient, companyA.FirstStoreId, "Company A counter");

        Assert.DoesNotContain("Company A counter", await companyB.OwnerClient.GetStringAsync(SecurityTestCompany.DevicesUrl()));
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.GetAsync(SecurityTestCompany.DevicesUrl(deviceId.ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await companyB.OwnerClient.PostAsync(SecurityTestCompany.DevicesUrl($"{deviceId}/authorize"), null)).StatusCode);
        // A store of another company is not valid for this company (the answer does not reveal that it exists).
        Assert.Equal(HttpStatusCode.BadRequest,
            (await companyB.OwnerClient.PostAsJsonAsync(SecurityTestCompany.DevicesUrl(), new { storeId = companyA.FirstStoreId, label = "Cross" })).StatusCode);
    }
}
