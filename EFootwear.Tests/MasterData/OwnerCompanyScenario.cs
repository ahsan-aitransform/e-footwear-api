using System.Net.Http.Json;
using System.Text.Json;
using EFootwear.Domain.Security;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.MasterData;

/// <summary>
/// A fresh company with its first store and a signed-in owner, plus helpers to sign in users who hold exactly the permission
/// codes a test names. Every call makes a new company, so tests never see each other's data.
/// Used by the integration tests of the master data, customers, vendors, accounting and cash modules.
/// </summary>
public sealed class OwnerCompanyScenario
{
    private readonly ApiTestEnvironment environment;

    private OwnerCompanyScenario(ApiTestEnvironment environment, TestCompany company, HttpClient platformClient, HttpClient ownerClient)
    {
        this.environment = environment;
        Company = company;
        PlatformClient = platformClient;
        OwnerClient = ownerClient;
    }

    public TestCompany Company { get; }
    public HttpClient PlatformClient { get; }
    public HttpClient OwnerClient { get; }
    public Guid CompanyId => Company.CompanyId;
    public Guid FirstStoreId => Company.FirstStoreId;

    public static async Task<OwnerCompanyScenario> CreateAsync(ApiTestEnvironment environment)
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var ownerClient = await environment.Scenarios.SignInAsync(company.OwnerEmail, company.OwnerTemporaryPassword);
        return new OwnerCompanyScenario(environment, company, platformClient, ownerClient);
    }

    /// <summary>A second store of the same company (created by the platform administrator).</summary>
    public Task<Guid> CreateStoreAsync(string code) => environment.Scenarios.CreateStoreAsync(PlatformClient, CompanyId, code);

    /// <summary>A signed-in user whose permission rows are exactly <paramref name="permissionCodes"/> and who works in the given stores.</summary>
    public async Task<HttpClient> SignInUserWithPermissionsAsync(string roleCode, IReadOnlyList<Guid> storeIds, params string[] permissionCodes)
    {
        var user = await environment.Scenarios.CreateUserAsync(OwnerClient, null, roleCode, storeIds);
        await environment.Scenarios.SetUserPermissionsAsync(CompanyId, user.UserId, permissionCodes);
        return await environment.Scenarios.SignInAsync(user.Email, user.TemporaryPassword);
    }

    public Task<HttpClient> SignInManagerWithPermissionsAsync(params string[] permissionCodes) =>
        SignInUserWithPermissionsAsync(RoleCodes.StoreManager, [FirstStoreId], permissionCodes);

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Posts a JSON body and fails the test with the server's message when the answer is not a success.</summary>
    public static async Task<JsonElement> PostJsonAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        await TestScenarioBuilder.EnsureSuccessAsync(response);
        return await ReadJsonAsync(response);
    }
}
