using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Security;

/// <summary>The exact JSON the frontend builds against: GET /auth/me and GET /permissions/catalog.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class CurrentSessionContractTests(ApiTestEnvironment environment)
{
    private static readonly string[] ExpectedCurrentSessionProperties =
    [
        "accessibleStoreIds", "audience", "companyId", "companyName", "displayName", "emailOrUsername",
        "grantedPermissionCodes", "hasAccessToAllStores", "mustChangePassword", "platformAdministratorId",
        "roleCode", "userId",
    ];

    private static readonly string CurrentSessionUrl =
        TestScenarioBuilder.Url(ApiRoutes.Authentication.Root, ApiRoutes.Authentication.CurrentSession);
    private static readonly string CatalogUrl =
        TestScenarioBuilder.Url(ApiRoutes.Permissions.Root, ApiRoutes.Permissions.Catalog);

    [Fact]
    public async Task Company_owner_session_has_the_exact_contract_and_every_permission()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient, "Contract Company");
        var ownerClient = await environment.Scenarios.SignInAsync(company.OwnerEmail, company.OwnerTemporaryPassword);

        var response = await ownerClient.GetAsync(CurrentSessionUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ExpectedCurrentSessionProperties, session.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("COMPANY_USER", session.GetProperty("audience").GetString());
        Assert.Equal("Test Owner", session.GetProperty("displayName").GetString());
        Assert.Equal(company.OwnerEmail, session.GetProperty("emailOrUsername").GetString());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("platformAdministratorId").ValueKind);
        Assert.Equal(company.CompanyId, session.GetProperty("companyId").GetGuid());
        Assert.Equal("Contract Company", session.GetProperty("companyName").GetString());
        Assert.Equal(RoleCodes.CompanyOwner, session.GetProperty("roleCode").GetString());
        Assert.True(session.GetProperty("hasAccessToAllStores").GetBoolean());
        Assert.False(session.GetProperty("mustChangePassword").GetBoolean());

        var accessibleStoreIds = session.GetProperty("accessibleStoreIds").EnumerateArray().Select(id => id.GetGuid()).ToList();
        Assert.Contains(company.FirstStoreId, accessibleStoreIds);
        Assert.Contains(Guid.Empty, accessibleStoreIds);   // head office

        var grantedCodes = session.GetProperty("grantedPermissionCodes").EnumerateArray().Select(code => code.GetString()).ToHashSet();
        Assert.Equal(PermissionCatalog.AllPermissionCodes.Count, grantedCodes.Count);
        Assert.Contains(PermissionCodes.Products.NewProduct.Add, grantedCodes);
    }

    [Fact]
    public async Task Store_manager_session_lists_only_the_assigned_store_and_the_template_permissions()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var secondStoreId = await environment.Scenarios.CreateStoreAsync(platformClient, company.CompanyId, "S2");
        var manager = await environment.Scenarios.CreateUserAsync(platformClient, company.CompanyId, RoleCodes.StoreManager, [secondStoreId]);
        var managerClient = await environment.Scenarios.SignInAsync(manager.Email, manager.TemporaryPassword);

        var session = await (await managerClient.GetAsync(CurrentSessionUrl)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(RoleCodes.StoreManager, session.GetProperty("roleCode").GetString());
        Assert.False(session.GetProperty("hasAccessToAllStores").GetBoolean());
        Assert.Equal([secondStoreId], session.GetProperty("accessibleStoreIds").EnumerateArray().Select(id => id.GetGuid()).ToArray());

        var grantedCodes = session.GetProperty("grantedPermissionCodes").EnumerateArray().Select(code => code.GetString()!).Order().ToArray();
        Assert.Equal(DefaultRolePermissionTemplates.PermissionCodesForRole(RoleCodes.StoreManager).Order().ToArray(), grantedCodes);
    }

    [Fact]
    public async Task Platform_administrator_session_has_no_company_and_no_permissions()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();

        var session = await (await platformClient.GetAsync(CurrentSessionUrl)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ExpectedCurrentSessionProperties, session.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("PLATFORM_ADMINISTRATOR", session.GetProperty("audience").GetString());
        Assert.Equal("Test Platform Administrator", session.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.String, session.GetProperty("platformAdministratorId").ValueKind);
        Assert.Equal(JsonValueKind.Null, session.GetProperty("companyId").ValueKind);
        Assert.Equal(JsonValueKind.Null, session.GetProperty("companyName").ValueKind);
        Assert.Equal(JsonValueKind.Null, session.GetProperty("userId").ValueKind);
        Assert.Equal(RoleCodes.PlatformAdmin, session.GetProperty("roleCode").GetString());
        Assert.Empty(session.GetProperty("grantedPermissionCodes").EnumerateArray());
        Assert.Empty(session.GetProperty("accessibleStoreIds").EnumerateArray());
    }

    [Fact]
    public async Task Session_requires_a_valid_token()
    {
        var anonymous = environment.Scenarios.CreateAnonymousClient();
        var response = await anonymous.GetAsync(CurrentSessionUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", (await TestScenarioBuilder.ReadProblemAsync(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_temporary_password_allows_only_the_auth_endpoints()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);

        var (client, mustChangePassword) = await environment.Scenarios.LoginAsync(company.OwnerEmail, company.OwnerTemporaryPassword);

        Assert.True(mustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(CurrentSessionUrl)).StatusCode);
        var blocked = await client.GetAsync(TestScenarioBuilder.Url(ApiRoutes.Stores.Root));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("PASSWORD_CHANGE_REQUIRED", (await TestScenarioBuilder.ReadProblemAsync(blocked)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Permission_catalog_is_the_tree_of_modules_screens_and_actions()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var response = await platformClient.GetAsync(CatalogUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalog = await response.Content.ReadFromJsonAsync<JsonElement>();

        var modules = catalog.GetProperty("modules").EnumerateArray().ToList();
        Assert.Equal(PermissionCatalog.Modules.Select(module => module.ModuleCode), modules.Select(module => module.GetProperty("moduleCode").GetString()));

        var module = modules.Single(candidate => candidate.GetProperty("moduleCode").GetString() == "REPORTS");
        Assert.Equal(["displayName", "moduleCode", "screens"], module.EnumerateObject().Select(property => property.Name).Order().ToArray());
        var screen = module.GetProperty("screens").EnumerateArray().Single(candidate => candidate.GetProperty("screenCode").GetString() == "ACCOUNT_LEDGER");
        Assert.Equal(["actions", "displayName", "screenCode", "subGroupCode"], screen.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("ACCOUNT_REPORTS", screen.GetProperty("subGroupCode").GetString());
        var action = screen.GetProperty("actions").EnumerateArray().First();
        Assert.Equal(["actionCode", "displayName", "permissionCode"], action.EnumerateObject().Select(property => property.Name).Order().ToArray());

        var totalActions = modules.Sum(candidate => candidate.GetProperty("screens").EnumerateArray().Sum(s => s.GetProperty("actions").GetArrayLength()));
        Assert.Equal(PermissionCatalog.AllPermissionCodes.Count, totalActions);
    }

    [Fact]
    public async Task Permission_catalog_needs_a_signed_in_person_but_no_permission()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await environment.Scenarios.CreateAnonymousClient().GetAsync(CatalogUrl)).StatusCode);

        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var cashier = await environment.Scenarios.CreateUserAsync(platformClient, company.CompanyId, RoleCodes.StoreManager, [company.FirstStoreId]);
        var managerClient = await environment.Scenarios.SignInAsync(cashier.Email, cashier.TemporaryPassword);
        Assert.Equal(HttpStatusCode.OK, (await managerClient.GetAsync(CatalogUrl)).StatusCode);
    }
}
