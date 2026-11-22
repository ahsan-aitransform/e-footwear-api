using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Tests.Infrastructure;
using EFootwear.Tests.Settings;

namespace EFootwear.Tests.Platform;

/// <summary>Plans, plan assignment (subscription), company status and the platform audit log. Platform administrators only.</summary>
[Collection(ApiIntegrationCollection.Name)]
public class PlatformAdministrationApiTests(ApiTestEnvironment environment)
{
    private static string NewPlanCode() => "PLAN" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<JsonElement> FindPlanAsync(HttpClient platformClient, string code)
    {
        var plans = await (await platformClient.GetAsync(SettingsUrls.PlatformPlans())).ReadSuccessJsonAsync();
        return plans.EnumerateArray().Single(plan => plan.GetProperty("code").GetString() == code);
    }

    private static async Task CreatePlanAsync(HttpClient platformClient, string code, int maximumStores = 5, int maximumUsers = 50)
    {
        var response = await platformClient.PostAsJsonAsync(SettingsUrls.PlatformPlans(), new { code, name = "Plan " + code, maximumStores, maximumUsers });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Plans_are_created_listed_updated_and_retired()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var code = NewPlanCode();

        await CreatePlanAsync(platformClient, code);
        var created = await FindPlanAsync(platformClient, code);
        Assert.Equal(5, created.GetProperty("maximumStores").GetInt32());
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Equal(0, created.GetProperty("companyCount").GetInt32());

        var duplicate = await platformClient.PostAsJsonAsync(SettingsUrls.PlatformPlans(), new { code, name = "Again", maximumStores = 1, maximumUsers = 1 });
        await duplicate.AssertProblemAsync(HttpStatusCode.Conflict, "CONFLICT");
        await (await platformClient.PostAsJsonAsync(SettingsUrls.PlatformPlans(), new { code = "bad code", name = "x", maximumStores = 1, maximumUsers = 1 }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await platformClient.PostAsJsonAsync(SettingsUrls.PlatformPlans(), new { code = NewPlanCode(), name = "x", maximumStores = 0, maximumUsers = 1 }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        var update = await platformClient.PutAsJsonAsync(SettingsUrls.PlatformPlans(code),
            new { name = "Gold plan", maximumStores = 10, maximumUsers = 100, isActive = true });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var updated = await FindPlanAsync(platformClient, code);
        Assert.Equal("Gold plan", updated.GetProperty("name").GetString());
        Assert.Equal(100, updated.GetProperty("maximumUsers").GetInt32());

        await (await platformClient.PutAsJsonAsync(SettingsUrls.PlatformPlans("NOSUCHPLAN"),
            new { name = "x", maximumStores = 1, maximumUsers = 1, isActive = true })).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");

        await platformClient.PutAsJsonAsync(SettingsUrls.PlatformPlans(code), new { name = "Gold plan", maximumStores = 10, maximumUsers = 100, isActive = false });
        Assert.False((await FindPlanAsync(platformClient, code)).GetProperty("isActive").GetBoolean());

        var scenario = await SettingsScenario.CreateAsync(environment);
        await (await scenario.PlatformClient.PutAsJsonAsync(
            SettingsUrls.PlatformCompany(scenario.Company.CompanyId, ApiRoutes.PlatformCompanyAdministration.Plan), new { planCode = code }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task A_plan_is_assigned_to_a_company_and_the_detail_shows_the_usage_against_its_limits()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var code = NewPlanCode();
        await CreatePlanAsync(scenario.PlatformClient, code, maximumStores: 3, maximumUsers: 30);

        var assign = await scenario.PlatformClient.PutAsJsonAsync(
            SettingsUrls.PlatformCompany(scenario.Company.CompanyId, ApiRoutes.PlatformCompanyAdministration.Plan), new { planCode = code });
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var detail = await (await scenario.PlatformClient.GetAsync(
            SettingsUrls.PlatformCompany(scenario.Company.CompanyId, ApiRoutes.PlatformCompanyAdministration.Detail))).ReadSuccessJsonAsync();
        Assert.Equal(code, detail.GetProperty("profile").GetProperty("planCode").GetString());
        var usage = detail.GetProperty("usage");
        Assert.Equal(code, usage.GetProperty("planCode").GetString());
        Assert.Equal(3, usage.GetProperty("maximumStores").GetInt32());
        Assert.Equal(30, usage.GetProperty("maximumUsers").GetInt32());
        Assert.Equal(1, usage.GetProperty("activeStores").GetInt32());
        Assert.Equal(1, usage.GetProperty("users").GetInt32());
        Assert.Equal(1, (await FindPlanAsync(scenario.PlatformClient, code)).GetProperty("companyCount").GetInt32());

        await (await scenario.PlatformClient.PutAsJsonAsync(
            SettingsUrls.PlatformCompany(scenario.Company.CompanyId, ApiRoutes.PlatformCompanyAdministration.Plan), new { planCode = "NOSUCHPLAN" }))
            .AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await scenario.PlatformClient.GetAsync(
            SettingsUrls.PlatformCompany(Guid.NewGuid(), ApiRoutes.PlatformCompanyAdministration.Detail))).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task A_suspended_company_is_locked_out_at_once_and_can_be_reactivated()
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var (ownerClient, _) = await environment.Scenarios.LoginAsync(company.OwnerEmail, company.OwnerTemporaryPassword);
        var currentSessionUrl = TestScenarioBuilder.Url(ApiRoutes.Authentication.Root, ApiRoutes.Authentication.CurrentSession);
        var statusUrl = SettingsUrls.PlatformCompany(company.CompanyId, ApiRoutes.PlatformCompanyAdministration.Status);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync(currentSessionUrl)).StatusCode);

        await (await platformClient.PostAsJsonAsync(statusUrl, new { status = "PAUSED", reason = (string?)null }))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        var suspend = await platformClient.PostAsJsonAsync(statusUrl, new { status = "SUSPENDED", reason = "Unpaid invoice" });
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);

        // the token that worked a moment ago is dead, and a new sign-in is refused
        await (await ownerClient.GetAsync(currentSessionUrl)).AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        var login = await environment.Scenarios.CreateAnonymousClient().PostAsJsonAsync(
            TestScenarioBuilder.Url(ApiRoutes.Authentication.Root, ApiRoutes.Authentication.Login),
            new { email = company.OwnerEmail, password = company.OwnerTemporaryPassword });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        var reactivate = await platformClient.PostAsJsonAsync(statusUrl, new { status = "ACTIVE", reason = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        var (again, _) = await environment.Scenarios.LoginAsync(company.OwnerEmail, company.OwnerTemporaryPassword);
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync(currentSessionUrl)).StatusCode);
    }

    [Fact]
    public async Task The_audit_log_records_plan_and_status_changes_and_can_be_filtered_and_paged()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var code = NewPlanCode();
        await CreatePlanAsync(scenario.PlatformClient, code);
        var planUrl = SettingsUrls.PlatformCompany(scenario.Company.CompanyId, ApiRoutes.PlatformCompanyAdministration.Plan);
        var statusUrl = SettingsUrls.PlatformCompany(scenario.Company.CompanyId, ApiRoutes.PlatformCompanyAdministration.Status);
        await scenario.PlatformClient.PutAsJsonAsync(planUrl, new { planCode = code });
        await scenario.PlatformClient.PostAsJsonAsync(statusUrl, new { status = "SUSPENDED", reason = "Unpaid invoice" });
        await scenario.PlatformClient.PostAsJsonAsync(statusUrl, new { status = "ACTIVE", reason = (string?)null });

        var all = await (await scenario.PlatformClient.GetAsync(
            SettingsUrls.PlatformAuditLog($"?companyId={scenario.Company.CompanyId}"))).ReadSuccessJsonAsync();
        var actions = all.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("actionCode").GetString()).ToList();
        Assert.Contains("COMPANY_CREATED", actions);
        Assert.Contains("COMPANY_PLAN_ASSIGNED", actions);
        Assert.Equal(2, actions.Count(action => action == "COMPANY_STATUS_CHANGED"));
        Assert.Equal(4, all.GetProperty("totalCount").GetInt64());

        var statusEntries = await (await scenario.PlatformClient.GetAsync(
            SettingsUrls.PlatformAuditLog($"?companyId={scenario.Company.CompanyId}&actionCode=COMPANY_STATUS_CHANGED"))).ReadSuccessJsonAsync();
        var items = statusEntries.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item.GetProperty("detail").GetProperty("reason").ValueKind == JsonValueKind.String
                                       && item.GetProperty("detail").GetProperty("reason").GetString() == "Unpaid invoice");
        Assert.All(items, item => Assert.Equal(scenario.Company.CompanyId, item.GetProperty("companyId").GetGuid()));
        Assert.All(items, item => Assert.False(string.IsNullOrEmpty(item.GetProperty("administratorUsername").GetString())));

        var firstPage = await (await scenario.PlatformClient.GetAsync(
            SettingsUrls.PlatformAuditLog($"?companyId={scenario.Company.CompanyId}&page=1&pageSize=1"))).ReadSuccessJsonAsync();
        Assert.Equal(1, firstPage.GetProperty("items").GetArrayLength());
        Assert.Equal(4, firstPage.GetProperty("totalCount").GetInt64());

        var future = await (await scenario.PlatformClient.GetAsync(
            SettingsUrls.PlatformAuditLog($"?companyId={scenario.Company.CompanyId}&from=2999-01-01T00:00:00Z"))).ReadSuccessJsonAsync();
        Assert.Equal(0, future.GetProperty("totalCount").GetInt64());

        await (await scenario.PlatformClient.GetAsync(SettingsUrls.PlatformAuditLog("?actionCode=NOPE")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await scenario.PlatformClient.GetAsync(SettingsUrls.PlatformAuditLog("?from=2030-01-01T00:00:00Z&to=2020-01-01T00:00:00Z")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Company_users_and_anonymous_callers_are_refused_on_every_platform_administration_endpoint()
    {
        var scenario = await SettingsScenario.CreateAsync(environment);
        var companyId = scenario.Company.CompanyId;
        var requests = new Func<HttpClient, Task<HttpResponseMessage>>[]
        {
            client => client.GetAsync(SettingsUrls.PlatformPlans()),
            client => client.PostAsJsonAsync(SettingsUrls.PlatformPlans(), new { code = "HACK", name = "x", maximumStores = 1, maximumUsers = 1 }),
            client => client.PutAsJsonAsync(SettingsUrls.PlatformPlans("STANDARD"), new { name = "x", maximumStores = 1, maximumUsers = 1, isActive = true }),
            client => client.GetAsync(SettingsUrls.PlatformCompany(companyId, ApiRoutes.PlatformCompanyAdministration.Detail)),
            client => client.PutAsJsonAsync(SettingsUrls.PlatformCompany(companyId, ApiRoutes.PlatformCompanyAdministration.Plan), new { planCode = "STANDARD" }),
            client => client.PostAsJsonAsync(SettingsUrls.PlatformCompany(companyId, ApiRoutes.PlatformCompanyAdministration.Status), new { status = "CLOSED", reason = (string?)null }),
            client => client.GetAsync(SettingsUrls.PlatformAuditLog()),
            client => client.GetAsync(SettingsUrls.PlatformBackup),
        };

        foreach (var request in requests)
            await (await request(scenario.OwnerClient)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        foreach (var request in requests)
            await (await request(environment.Scenarios.CreateAnonymousClient())).AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHORIZED");

        var profile = await (await scenario.OwnerClient.GetAsync(SettingsUrls.CompanyProfile)).ReadSuccessJsonAsync();
        Assert.Equal("ACTIVE", profile.GetProperty("status").GetString());   // nothing the refused calls tried has happened
    }
}
