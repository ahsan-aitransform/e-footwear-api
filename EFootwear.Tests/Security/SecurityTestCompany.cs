using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.Domain.Security;
using EFootwear.Tests.Infrastructure;

namespace EFootwear.Tests.Security;

/// <summary>
/// One company for a security test, created through the real API: its platform administrator, owner (signed in) and first store, plus
/// helpers to add users and stores and to build the URLs of the security endpoints.
/// </summary>
public sealed class SecurityTestCompany
{
    public const string PasswordAfterFirstSignIn = TestScenarioBuilder.TemporaryPassword + "-Changed9";

    private readonly ApiTestEnvironment environment;

    private SecurityTestCompany(ApiTestEnvironment environment, HttpClient platformClient, TestCompany company, HttpClient ownerClient)
    {
        this.environment = environment;
        PlatformClient = platformClient;
        Company = company;
        OwnerClient = ownerClient;
    }

    public HttpClient PlatformClient { get; }
    public TestCompany Company { get; }
    public HttpClient OwnerClient { get; }
    public Guid CompanyId => Company.CompanyId;
    public Guid FirstStoreId => Company.FirstStoreId;

    public static async Task<SecurityTestCompany> CreateAsync(ApiTestEnvironment environment)
    {
        var platformClient = await environment.Scenarios.CreatePlatformAdministratorClientAsync();
        var company = await environment.Scenarios.CreateCompanyAsync(platformClient);
        var ownerClient = await environment.Scenarios.SignInAsync(company.OwnerEmail, company.OwnerTemporaryPassword);
        return new SecurityTestCompany(environment, platformClient, company, ownerClient);
    }

    public Task<Guid> CreateStoreAsync() =>
        environment.Scenarios.CreateStoreAsync(PlatformClient, CompanyId, "S" + Guid.NewGuid().ToString("N")[..6]);

    /// <summary>A user created by the owner, who has NOT signed in yet.</summary>
    public Task<TestUser> CreateUserAsync(string roleCode, Guid? storeId = null) =>
        environment.Scenarios.CreateUserAsync(OwnerClient, null, roleCode, [storeId ?? FirstStoreId]);

    /// <summary>A user created by the owner, given exactly these permission codes, and signed in.</summary>
    public async Task<(TestUser User, HttpClient Client)> CreateSignedInUserAsync(string roleCode, Guid? storeId = null,
                                                                                  params string[] permissionCodes)
    {
        var user = await CreateUserAsync(roleCode, storeId);
        await environment.Scenarios.SetUserPermissionsAsync(CompanyId, user.UserId, permissionCodes);
        var client = await environment.Scenarios.SignInAsync(user.Email, user.TemporaryPassword);
        return (user, client);
    }

    public Task<(HttpClient Client, bool MustChangePassword)> LoginAsync(string email, string password) =>
        environment.Scenarios.LoginAsync(email, password);

    public HttpClient CreateAnonymousClient() => environment.Scenarios.CreateAnonymousClient();

    public Task<T?> QueryScalarAsync<T>(string sql, params object?[] parameters) =>
        environment.Database.QueryScalarAsOwnerAsync<T>(sql, parameters);

    public static string UsersUrl(string suffix = "") => "/" + ApiRoutes.Users.Root + (suffix.Length == 0 ? "" : "/" + suffix);

    public static string UserUrl(Guid userId, string suffix = "") => UsersUrl(userId.ToString() + (suffix.Length == 0 ? "" : "/" + suffix));

    public static string AccountUrl(string suffix) => "/" + ApiRoutes.Account.Root + "/" + suffix;

    public static string RoleTemplatesUrl(string suffix = "") => "/" + ApiRoutes.RoleTemplates.Root + (suffix.Length == 0 ? "" : "/" + suffix);

    public static string DevicesUrl(string suffix = "") => "/" + ApiRoutes.Devices.Root + (suffix.Length == 0 ? "" : "/" + suffix);

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<long> ReadRowVersionAsync(HttpClient client, Guid userId) =>
        (await ReadAsync(await client.GetAsync(UserUrl(userId)))).GetProperty("rowVersion").GetInt64();
}
