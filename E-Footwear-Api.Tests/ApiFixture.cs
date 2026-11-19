using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using E_Footwear_Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace E_Footwear_Api.Tests;

/// <summary>
/// Runs the real API against a real PostgreSQL 16 database that has FootwearPOS_schema_postgresql.sql applied.
/// Set EFW_TEST_DB to a superuser connection string of a scratch database (never a production one).
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>
{
    public const string PlatformUser = "platform-test";
    public const string PlatformPassword = "platform-test-password-1";
    public static readonly string ConnString =
        Environment.GetEnvironmentVariable("EFW_TEST_DB")
        ?? throw new InvalidOperationException("Set EFW_TEST_DB to a scratch PostgreSQL connection string.");

    public ApiFixture()
    {
        // Program.Main reads configuration at startup, so test settings are supplied as environment variables.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnString);
        Environment.SetEnvironmentVariable("Jwt__Key", "test-only-signing-key-0123456789-abcdefghij");
        Environment.SetEnvironmentVariable("Security__MaxFailedLogins", "3");
        Environment.SetEnvironmentVariable("Security__LockoutMinutes", "15");
        Environment.SetEnvironmentVariable("Security__LoginPermitsPerMinute", "1000");
        using var conn = new NpgsqlConnection(ConnString);
        conn.Open();
        conn.Execute("insert into platform.platform_users(user_name,password_hash) values (@u,@h) on conflict do nothing",
            new { u = PlatformUser, h = PasswordHasher.Hash(PlatformPassword) });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    public static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    public async Task<HttpClient> PlatformClient()
    {
        var c = CreateClient();
        var r = await c.PostAsJsonAsync("/api/v1/platform/auth/login", new { userName = PlatformUser, password = PlatformPassword });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("accessToken").GetString());
        return c;
    }

    public sealed record Company(string Code, long CompanyId, long StoreId, long OwnerId, string OwnerName, string OwnerPassword);

    public async Task<Company> RegisterCompany(HttpClient platform)
    {
        var code = Unique("co").ToLowerInvariant();
        var owner = "owner";
        var pw = "owner-password-123";
        var r = await platform.PostAsJsonAsync("/api/v1/platform/companies", new
        {
            code, name = "Test " + code, firstStoreCode = "MAIN", firstStoreName = "Main Store",
            ownerUserName = owner, ownerPassword = pw
        });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        return new Company(code, j.GetProperty("companyId").GetInt64(), j.GetProperty("firstStoreId").GetInt64(),
            j.GetProperty("ownerUserId").GetInt64(), owner, pw);
    }

    public async Task<(HttpClient client, JsonElement session)> Login(string company, string user, string password, long? storeId = null)
    {
        var c = CreateClient();
        var r = await c.PostAsJsonAsync("/api/v1/auth/login", new { companyCode = company, userName = user, password, storeId });
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("accessToken").GetString());
        return (c, j);
    }
}
