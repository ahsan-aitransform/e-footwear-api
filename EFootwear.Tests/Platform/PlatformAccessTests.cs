using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Platform;

/// <summary>Only a platform administrator ("super admin") reaches /platform; everyone else gets a clear, uniform error.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class PlatformAccessTests(ApiTestEnvironment environment)
{
    private static readonly string CompaniesUrl = TestScenarioBuilder.Url(ApiRoutes.Platform.Root, ApiRoutes.Platform.Companies);

    private static string CompanyStoresUrl(Guid companyId) => TestScenarioBuilder.Url(
        ApiRoutes.Platform.Root, ApiRoutes.Platform.CompanyStores.Replace("{companyId:guid}", companyId.ToString()));

    [Fact]
    public async Task Without_a_token_every_platform_endpoint_answers_401_with_an_error_code()
    {
        var anonymous = environment.Scenarios.CreateAnonymousClient();

        foreach (var response in new[]
        {
            await anonymous.GetAsync(CompaniesUrl),
            await anonymous.PostAsJsonAsync(CompaniesUrl, new { }),
            await anonymous.GetAsync(CompanyStoresUrl(Guid.NewGuid())),
            await anonymous.PostAsync(TestScenarioBuilder.Url(ApiRoutes.PlatformAuthentication.Root, ApiRoutes.PlatformAuthentication.Logout), null),
        })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var problem = await TestScenarioBuilder.ReadProblemAsync(response);
            Assert.Equal("UNAUTHORIZED", problem.GetProperty("errorCode").GetString());
            Assert.Equal(401, problem.GetProperty("status").GetInt32());
        }
    }

    [Fact]
    public async Task A_company_token_is_refused_on_every_platform_endpoint_with_403()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var ownerClient = await environment.Scenarios.SignInAsync(company.OwnerEmail, company.OwnerTemporaryPassword);

        foreach (var response in new[]
        {
            await ownerClient.GetAsync(CompaniesUrl),
            await ownerClient.PostAsJsonAsync(CompaniesUrl, new { name = "Hijack" }),
            await ownerClient.GetAsync(CompanyStoresUrl(company.CompanyId)),
            await ownerClient.PostAsJsonAsync(CompanyStoresUrl(company.CompanyId), new { code = "X", name = "X" }),
            await ownerClient.PostAsync(TestScenarioBuilder.Url(ApiRoutes.PlatformAuthentication.Root, ApiRoutes.PlatformAuthentication.Logout), null),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var problem = await TestScenarioBuilder.ReadProblemAsync(response);
            Assert.Equal("FORBIDDEN", problem.GetProperty("errorCode").GetString());
            Assert.Equal(403, problem.GetProperty("status").GetInt32());
        }
    }

    [Fact]
    public async Task A_platform_administrator_can_list_and_create()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);

        var companies = await (await platformClient.GetAsync(CompaniesUrl)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(company.CompanyId, companies.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));

        await environment.Scenarios.CreateStoreAsync(platformClient, company.CompanyId, "NEW");
        var stores = await (await platformClient.GetAsync(CompanyStoresUrl(company.CompanyId))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("NEW", stores.EnumerateArray().Select(item => item.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task A_platform_administrator_is_refused_on_company_only_endpoints()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();

        var stores = await platformClient.GetAsync(TestScenarioBuilder.Url(ApiRoutes.Stores.Root));
        Assert.Equal(HttpStatusCode.Forbidden, stores.StatusCode);

        var changePassword = await platformClient.PostAsJsonAsync(
            TestScenarioBuilder.Url(ApiRoutes.Authentication.Root, ApiRoutes.Authentication.ChangePassword),
            new { currentPassword = "x", newPassword = "yyyyyyyy" });
        Assert.Equal(HttpStatusCode.Forbidden, changePassword.StatusCode);
    }

    [Fact]
    public async Task Errors_share_one_format_with_error_code_and_correlation_id()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();

        var notFound = await platformClient.GetAsync(CompanyStoresUrl(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        var problem = await TestScenarioBuilder.ReadProblemAsync(notFound);
        Assert.Equal("NOT_FOUND", problem.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("correlationId").GetString()));
        Assert.Equal("Not found", problem.GetProperty("title").GetString());

        var invalid = await platformClient.PostAsJsonAsync(CompaniesUrl, new
        {
            name = "", timezone = (string?)null, ownerEmail = "bad", ownerFullName = "", ownerPassword = "x",
            firstStoreCode = "", firstStoreName = "",
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await TestScenarioBuilder.ReadProblemAsync(invalid)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Wrong_sign_in_answers_401_and_a_second_company_cannot_reuse_an_email()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var anonymous = environment.Scenarios.CreateAnonymousClient();

        var wrong = await anonymous.PostAsJsonAsync(
            TestScenarioBuilder.Url(ApiRoutes.Authentication.Root, ApiRoutes.Authentication.Login),
            new { email = company.OwnerEmail, password = "not-the-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var duplicate = await platformClient.PostAsJsonAsync(CompaniesUrl, new
        {
            name = "Other", timezone = (string?)null, ownerEmail = company.OwnerEmail, ownerFullName = "Dup",
            ownerPassword = TestScenarioBuilder.TemporaryPassword, firstStoreCode = "S1", firstStoreName = "S",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("CONFLICT", (await TestScenarioBuilder.ReadProblemAsync(duplicate)).GetProperty("errorCode").GetString());
    }
}
