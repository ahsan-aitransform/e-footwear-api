using System.Net;
using System.Net.Http.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Security;

/// <summary>Who may call which endpoint: role templates, explicit permission sets, the owner, and the platform administrator.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class PermissionMatrixTests(ApiTestEnvironment environment)
{
    private static readonly string UsersUrl = TestScenarioBuilder.Url(ApiRoutes.Users.Root);
    private static readonly string CreationOptionsUrl = TestScenarioBuilder.Url(ApiRoutes.Users.Root, ApiRoutes.Users.CreationOptions);
    private static readonly string StoresUrl = TestScenarioBuilder.Url(ApiRoutes.Stores.Root);

    private sealed record Company(TestCompany Details, HttpClient PlatformClient, HttpClient OwnerClient);

    private async Task<Company> CreateCompanyAsync()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var ownerClient = await environment.Scenarios.SignInAsync(company.OwnerEmail, company.OwnerTemporaryPassword);
        return new Company(company, platformClient, ownerClient);
    }

    private static object NewCashierBody(Guid storeId) => new
    {
        email = $"cashier.{Guid.NewGuid():N}@example.test",
        fullName = "New Cashier",
        roleCode = RoleCodes.CashierSalesman,
        storeIds = new[] { storeId },
        password = TestScenarioBuilder.TemporaryPassword,
    };

    private async Task<HttpClient> SignInUserWithPermissionsAsync(Company company, string roleCode, params string[] permissionCodes)
    {
        var user = await environment.Scenarios.CreateUserAsync(company.OwnerClient, null, roleCode, [company.Details.FirstStoreId]);
        await environment.Scenarios.SetUserPermissionsAsync(company.Details.CompanyId, user.UserId, permissionCodes);
        return await environment.Scenarios.SignInAsync(user.Email, user.TemporaryPassword);
    }

    [Fact]
    public async Task Owner_holds_every_permission_without_any_permission_rows()
    {
        var company = await CreateCompanyAsync();
        var ownerUserId = await environment.Database.QueryScalarAsOwnerAsync<Guid>(
            "select id from sec.users where company_id = $1 and role_code = 'COMPANY_OWNER'", company.Details.CompanyId);

        Assert.Equal(0L, await environment.Scenarios.CountUserPermissionsAsync(company.Details.CompanyId, ownerUserId));
        Assert.Equal(HttpStatusCode.OK, (await company.OwnerClient.GetAsync(UsersUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await company.OwnerClient.GetAsync(CreationOptionsUrl)).StatusCode);
    }

    [Fact]
    public async Task Users_endpoints_allow_exactly_the_roles_whose_template_grants_them()
    {
        var company = await CreateCompanyAsync();
        var storeId = company.Details.FirstStoreId;

        var managerClient = await SignInUserWithTemplateAsync(company, RoleCodes.StoreManager);
        var cashierClient = await SignInUserWithTemplateAsync(company, RoleCodes.CashierSalesman);
        var accountantClient = await SignInUserWithTemplateAsync(company, RoleCodes.Accountant);

        // list users
        Assert.Equal(HttpStatusCode.OK, (await managerClient.GetAsync(UsersUrl)).StatusCode);
        await AssertPermissionDeniedAsync(await cashierClient.GetAsync(UsersUrl), PermissionCodes.UserAccount.ViewUsers.View);
        await AssertPermissionDeniedAsync(await accountantClient.GetAsync(UsersUrl), PermissionCodes.UserAccount.ViewUsers.View);

        // creation options
        Assert.Equal(HttpStatusCode.OK, (await managerClient.GetAsync(CreationOptionsUrl)).StatusCode);
        await AssertPermissionDeniedAsync(await cashierClient.GetAsync(CreationOptionsUrl), PermissionCodes.UserAccount.CreateUser.Add);

        // create user
        Assert.Equal(HttpStatusCode.Created, (await managerClient.PostAsJsonAsync(UsersUrl, NewCashierBody(storeId))).StatusCode);
        await AssertPermissionDeniedAsync(await cashierClient.PostAsJsonAsync(UsersUrl, NewCashierBody(storeId)), PermissionCodes.UserAccount.CreateUser.Add);
        await AssertPermissionDeniedAsync(await accountantClient.PostAsJsonAsync(UsersUrl, NewCashierBody(storeId)), PermissionCodes.UserAccount.CreateUser.Add);

        // every signed-in company user may read their stores
        Assert.Equal(HttpStatusCode.OK, (await cashierClient.GetAsync(StoresUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await accountantClient.GetAsync(StoresUrl)).StatusCode);
    }

    [Fact]
    public async Task A_single_explicit_permission_opens_only_its_own_endpoint()
    {
        var company = await CreateCompanyAsync();
        var storeId = company.Details.FirstStoreId;

        var viewerClient = await SignInUserWithPermissionsAsync(company, RoleCodes.StoreManager, PermissionCodes.UserAccount.ViewUsers.View);
        Assert.Equal(HttpStatusCode.OK, (await viewerClient.GetAsync(UsersUrl)).StatusCode);
        await AssertPermissionDeniedAsync(await viewerClient.GetAsync(CreationOptionsUrl), PermissionCodes.UserAccount.CreateUser.Add);
        await AssertPermissionDeniedAsync(await viewerClient.PostAsJsonAsync(UsersUrl, NewCashierBody(storeId)), PermissionCodes.UserAccount.CreateUser.Add);

        var creatorClient = await SignInUserWithPermissionsAsync(company, RoleCodes.StoreManager, PermissionCodes.UserAccount.CreateUser.Add);
        Assert.Equal(HttpStatusCode.Created, (await creatorClient.PostAsJsonAsync(UsersUrl, NewCashierBody(storeId))).StatusCode);
        await AssertPermissionDeniedAsync(await creatorClient.GetAsync(UsersUrl), PermissionCodes.UserAccount.ViewUsers.View);

        var nothingClient = await SignInUserWithPermissionsAsync(company, RoleCodes.StoreManager);
        await AssertPermissionDeniedAsync(await nothingClient.GetAsync(UsersUrl), PermissionCodes.UserAccount.ViewUsers.View);
    }

    [Fact]
    public async Task A_permission_removed_after_sign_in_takes_effect_on_the_next_request()
    {
        var company = await CreateCompanyAsync();
        var user = await environment.Scenarios.CreateUserAsync(company.OwnerClient, null, RoleCodes.StoreManager, [company.Details.FirstStoreId]);
        var client = await environment.Scenarios.SignInAsync(user.Email, user.TemporaryPassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(UsersUrl)).StatusCode);

        await environment.Scenarios.SetUserPermissionsAsync(company.Details.CompanyId, user.UserId, []);

        await AssertPermissionDeniedAsync(await client.GetAsync(UsersUrl), PermissionCodes.UserAccount.ViewUsers.View);
    }

    [Fact]
    public async Task The_platform_administrator_may_use_the_user_endpoints_for_a_named_company()
    {
        var company = await CreateCompanyAsync();

        Assert.Equal(HttpStatusCode.OK, (await company.PlatformClient.GetAsync($"{UsersUrl}?companyId={company.Details.CompanyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await company.PlatformClient.GetAsync($"{CreationOptionsUrl}?companyId={company.Details.CompanyId}")).StatusCode);
    }

    [Fact]
    public async Task Creating_a_user_copies_the_role_template_into_their_own_permissions()
    {
        var company = await CreateCompanyAsync();

        foreach (var roleCode in new[] { RoleCodes.StoreManager, RoleCodes.CashierSalesman, RoleCodes.Accountant })
        {
            var user = await environment.Scenarios.CreateUserAsync(company.OwnerClient, null, roleCode, [company.Details.FirstStoreId]);
            Assert.Equal((long)DefaultRolePermissionTemplates.PermissionCodesForRole(roleCode).Count,
                         await environment.Scenarios.CountUserPermissionsAsync(company.Details.CompanyId, user.UserId));
        }
    }

    private async Task<HttpClient> SignInUserWithTemplateAsync(Company company, string roleCode)
    {
        var user = await environment.Scenarios.CreateUserAsync(company.OwnerClient, null, roleCode, [company.Details.FirstStoreId]);
        return await environment.Scenarios.SignInAsync(user.Email, user.TemporaryPassword);
    }

    private static async Task AssertPermissionDeniedAsync(HttpResponseMessage response, string expectedPermissionCode)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await TestScenarioBuilder.ReadProblemAsync(response);
        Assert.Equal("PERMISSION_DENIED", problem.GetProperty("errorCode").GetString());
        Assert.Contains(expectedPermissionCode, problem.GetProperty("detail").GetString());
    }
}
