using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Routing;
using EFootwear.DAL.Security;
using EFootwear.Domain.Security;

namespace EFootwear.Tests.Infrastructure;

/// <summary>
/// Builds the situations the integration tests need, through the real API wherever possible: a platform administrator,
/// companies, stores, users with a role, and signed-in HTTP clients. Every company and user gets a unique name, so tests
/// never collide even though they share one database.
/// </summary>
public sealed class TestScenarioBuilder(ApiTestEnvironment environment)
{
    public const string TemporaryPassword = "Temporary#1234";
    private const string PasswordSuffixAfterChange = "-Changed9";

    private readonly Argon2PasswordHasher passwordHasher = new();

    public static string Url(string root, string segment = "") =>
        string.IsNullOrEmpty(segment) ? "/" + root : $"/{root}/{segment}";

    public HttpClient CreateAnonymousClient() => environment.ApiFactory.CreateClient();

    /// <summary>Creates a platform administrator directly in the database (there is no API for that) and signs in.</summary>
    public async Task<HttpClient> CreatePlatformAdministratorClientAsync()
    {
        var username = "admin-" + Guid.NewGuid().ToString("N")[..10];
        const string password = "Platform#Admin1";
        await environment.Database.ExecuteAsOwnerAsync(
            "insert into platform.admins (username, password_hash, full_name) values ($1, $2, $3)",
            username, passwordHasher.Hash(password), "Test Platform Administrator");

        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync(
            Url(ApiRoutes.PlatformAuthentication.Root, ApiRoutes.PlatformAuthentication.Login), new { username, password });
        response.EnsureSuccessStatusCode();
        var accessToken = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public async Task<TestCompany> CreateCompanyAsync(HttpClient platformAdministratorClient, string? companyName = null)
    {
        var unique = Guid.NewGuid().ToString("N")[..10];
        var ownerEmail = $"owner.{unique}@example.test";
        var response = await platformAdministratorClient.PostAsJsonAsync(
            Url(ApiRoutes.Platform.Root, ApiRoutes.Platform.Companies), new
            {
                name = companyName ?? $"Test Company {unique}",
                timezone = (string?)null,
                ownerEmail,
                ownerFullName = "Test Owner",
                ownerPassword = TemporaryPassword,
                firstStoreCode = "S1",
                firstStoreName = "First Store",
            });
        await EnsureSuccessAsync(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new TestCompany(body.GetProperty("companyId").GetGuid(), body.GetProperty("firstStoreId").GetGuid(),
                               ownerEmail, TemporaryPassword);
    }

    public async Task<Guid> CreateStoreAsync(HttpClient platformAdministratorClient, Guid companyId, string code)
    {
        var path = ApiRoutes.Platform.CompanyStores.Replace("{companyId:guid}", companyId.ToString());
        var response = await platformAdministratorClient.PostAsJsonAsync(
            Url(ApiRoutes.Platform.Root, path), new { code, name = "Store " + code });
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Signs in. A temporary password is changed first (the API refuses everything else until then), then signs in again.</summary>
    public async Task<HttpClient> SignInAsync(string email, string password)
    {
        var login = await LoginAsync(email, password);
        if (!login.MustChangePassword) return login.Client;

        var newPassword = password + PasswordSuffixAfterChange;
        var change = await login.Client.PostAsJsonAsync(
            Url(ApiRoutes.Authentication.Root, ApiRoutes.Authentication.ChangePassword),
            new { currentPassword = password, newPassword });
        await EnsureSuccessAsync(change);
        return (await LoginAsync(email, newPassword)).Client;
    }

    /// <summary>Signs in and returns the client together with the flag the API reported, without changing a temporary password.</summary>
    public async Task<(HttpClient Client, bool MustChangePassword)> LoginAsync(string email, string password)
    {
        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync(
            Url(ApiRoutes.Authentication.Root, ApiRoutes.Authentication.Login), new { email, password });
        await EnsureSuccessAsync(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString()!);
        return (client, body.GetProperty("mustChangePassword").GetBoolean());
    }

    /// <summary>Creates a user through the API (as the owner, a manager or a platform administrator).</summary>
    public async Task<TestUser> CreateUserAsync(HttpClient creatorClient, Guid? companyId, string roleCode,
                                                IReadOnlyList<Guid> storeIds)
    {
        var email = $"{roleCode.ToLowerInvariant()}.{Guid.NewGuid().ToString("N")[..10]}@example.test";
        var response = await creatorClient.PostAsJsonAsync(Url(ApiRoutes.Users.Root), new
        {
            companyId,
            email,
            fullName = "Test " + roleCode,
            roleCode,
            storeIds,
            password = TemporaryPassword,
        });
        await EnsureSuccessAsync(response);
        var userId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return new TestUser(userId, email, TemporaryPassword, roleCode);
    }

    /// <summary>Replaces ALL of a user's permission rows with exactly these codes (done directly in the database).</summary>
    public async Task SetUserPermissionsAsync(Guid companyId, Guid userId, IEnumerable<string> permissionCodes)
    {
        await environment.Database.ExecuteAsOwnerAsync(
            "delete from sec.user_permissions where company_id = $1 and user_id = $2", companyId, userId);
        foreach (var permissionCode in permissionCodes)
            await environment.Database.ExecuteAsOwnerAsync(
                "insert into sec.user_permissions (company_id, user_id, permission_code) values ($1, $2, $3)",
                companyId, userId, permissionCode);
    }

    public async Task<long> CountUserPermissionsAsync(Guid companyId, Guid userId) =>
        await environment.Database.QueryScalarAsOwnerAsync<long>(
            "select count(*) from sec.user_permissions where company_id = $1 and user_id = $2", companyId, userId);

    public static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new HttpRequestException(
            $"{(int)response.StatusCode} {response.StatusCode} from {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: " +
            await response.Content.ReadAsStringAsync());
    }

    public static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static void AssertStatus(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode} for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}");
}
