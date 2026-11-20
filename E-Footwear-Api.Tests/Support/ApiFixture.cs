using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EFootwear.Api.Security;
using EFootwear.Infrastructure.Persistence;
using EFootwear.Infrastructure.Provisioning;
using EFootwear.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace EFootwear.Api.Tests.Support;

/// <summary>
/// Spins up a scratch PostgreSQL database (the v6 baseline applied unchanged) and the real API in-process.
/// Set EFW_TEST_ADMIN to a superuser connection string of a SCRATCH server, e.g.
/// "Host=localhost;Port=55432;Username=postgres;Password=...". Nothing else is touched.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string OwnerPassword = "Owner-Pass-1";
    private readonly string _admin = Environment.GetEnvironmentVariable("EFW_TEST_ADMIN")
        ?? throw new InvalidOperationException("Set EFW_TEST_ADMIN to a superuser connection string of a scratch PostgreSQL server.");
    private readonly string _dbName = "efw_test_" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _role = "efw_t_" + Guid.NewGuid().ToString("N")[..10];
    private readonly string _rolePw = Guid.NewGuid().ToString("N");
    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Superuser connection to the scratch database (bypasses RLS: for arranging and inspecting data only).</summary>
    public string OwnerConnection => new NpgsqlConnectionStringBuilder(_admin) { Database = _dbName }.ToString();
    /// <summary>The login the API itself uses (member of pos_app, no BYPASSRLS).</summary>
    public string ApiConnection => new NpgsqlConnectionStringBuilder(_admin) { Database = _dbName, Username = _role, Password = _rolePw }.ToString();

    public ProvisionedCompany Acme { get; private set; } = null!;
    public ProvisionedCompany Globex { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await using (var c = new NpgsqlConnection(_admin))
        {
            await c.OpenAsync();
            await Exec(c, $"create database \"{_dbName}\"");
            await Exec(c, $"create role \"{_role}\" login password '{_rolePw}' nosuperuser nobypassrls");
        }
        await Migrator.RunAsync(OwnerConnection, FindMigrations(), _role);

        var hasher = new Argon2PasswordHasher();
        Acme = await CompanyProvisioner.CreateAsync(OwnerConnection,
            new NewCompany("Acme Footwear", "MAIN", "Main Store", "owner", "Acme Owner", OwnerPassword, OwnerMustChangePassword: false), hasher);
        Globex = await CompanyProvisioner.CreateAsync(OwnerConnection,
            new NewCompany("Globex Shoes", "G1", "Globex Store", "owner", "Globex Owner", OwnerPassword, OwnerMustChangePassword: false), hasher);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            // UseSetting (not ConfigureAppConfiguration): Program.cs reads configuration eagerly, before late overrides apply.
            b.UseSetting("ConnectionStrings:Default", ApiConnection);
            b.UseSetting("Security:DisableHttpsRedirect", "true");
            b.UseSetting("Workers:PriceRefresh", "false");   // tests rebuild explicitly
            b.UseSetting("Security:LoginRateLimitPerMinute", "100000");
        });
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(_admin);
        await c.OpenAsync();
        await Exec(c, $"drop database if exists \"{_dbName}\" with (force)");
        await Exec(c, $"drop role if exists \"{_role}\"");
    }

    public HttpClient NewClient() => _factory.CreateClient();

    // ---------------------------------------------------------------- helpers used by the tests

    public async Task<Session> LoginAsync(string company, string user, string password, long? storeId = null)
    {
        var c = NewClient();
        var r = await c.PostAsJsonAsync("/api/v1/auth/login", new { company, userName = user, password });
        var body = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"Login failed {(int)r.StatusCode}: {body}");
        var j = JsonDocument.Parse(body).RootElement;
        var token = j.GetProperty("token").GetString()!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var s = new Session(c, token, j.GetProperty("me").Clone());
        if (storeId is { } id) c.DefaultRequestHeaders.Add(SessionDefaults.StoreHeader, id.ToString());
        return s;
    }

    public Task<Session> OwnerAsync(long? storeId = null) => LoginAsync("Acme Footwear", "owner", OwnerPassword, storeId);

    /// <summary>Runs SQL as the database owner (no RLS). For arranging data the API has no endpoint for.</summary>
    public async Task<T?> ScalarAsync<T>(string sql, params (string, object)[] p)
    {
        await using var c = new NpgsqlConnection(OwnerConnection);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v);
        var o = await cmd.ExecuteScalarAsync();
        return o is null or DBNull ? default : (T)Convert.ChangeType(o, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    public async Task ExecAsync(string sql, params (string, object)[] p) => await ScalarAsync<object>(sql, p);

    private static async Task Exec(NpgsqlConnection c, string sql) { await using var cmd = new NpgsqlCommand(sql, c); await cmd.ExecuteNonQueryAsync(); }

    private static string FindMigrations()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "db", "migrations");
            if (Directory.Exists(p)) return p;
        }
        throw new DirectoryNotFoundException("db/migrations not found above the test binaries.");
    }
}

public sealed record Session(HttpClient Http, string Token, JsonElement Me)
{
    public long StoreId => Me.GetProperty("currentStore").GetProperty("id").GetInt64();
    public long UserId => Me.GetProperty("userId").GetInt64();
    public long CompanyId => Me.GetProperty("company").GetProperty("id").GetInt64();

    public async Task<JsonElement> GetJsonAsync(string url)
    {
        var r = await Http.GetAsync(url);
        var s = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"GET {url} -> {(int)r.StatusCode}: {s}");
        return JsonDocument.Parse(s).RootElement.Clone();
    }

    public async Task<(System.Net.HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        var r = await Http.SendAsync(req);
        var s = await r.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(s) ? default : JsonDocument.Parse(s).RootElement.Clone();
        return (r.StatusCode, json);
    }

    public Task<(System.Net.HttpStatusCode Status, JsonElement Body)> PostAsync(string url, object? body = null) => SendAsync(HttpMethod.Post, url, body);
    public Task<(System.Net.HttpStatusCode Status, JsonElement Body)> PutAsync(string url, object? body = null) => SendAsync(HttpMethod.Put, url, body);
    public Task<(System.Net.HttpStatusCode Status, JsonElement Body)> DeleteAsync(string url) => SendAsync(HttpMethod.Delete, url);
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
