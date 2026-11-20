using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EFootwear.Api;
using EFootwear.Api.Tests.Support;
using EFootwear.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace EFootwear.Api.Tests;

[Collection("api")]
public sealed class FoundationTests(ApiFixture fx)
{
    private async Task<(HttpStatusCode, JsonElement)> RawLogin(string company, string user, string pw)
    {
        using var c = fx.NewClient();
        var r = await c.PostAsJsonAsync("/api/v1/auth/login", new { company, userName = user, password = pw });
        var s = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, string.IsNullOrWhiteSpace(s) ? default : JsonDocument.Parse(s).RootElement.Clone());
    }

    [Fact]
    public async Task Migrations_are_idempotent()
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "db", "migrations"))) { dir = Path.Combine(d.FullName, "db", "migrations"); break; }
        Assert.Empty(await Migrator.RunAsync(fx.OwnerConnection, dir, null));
    }

    [Fact]
    public async Task Owner_logs_in_by_company_name_and_by_id_and_sees_every_permission_and_store()
    {
        var byName = await fx.LoginAsync("Acme Footwear", "owner", ApiFixture.OwnerPassword);
        var byId = await fx.LoginAsync(fx.Acme.CompanyId.ToString(), "OWNER", ApiFixture.OwnerPassword);   // user name is case-insensitive
        Assert.Equal(fx.Acme.CompanyId, byName.CompanyId);
        Assert.Equal(byName.UserId, byId.UserId);
        Assert.True(byName.Me.GetProperty("isOwner").GetBoolean());
        Assert.True(byName.Me.GetProperty("permissions").GetArrayLength() >= 30);
        var stores = byName.Me.GetProperty("stores").EnumerateArray().Select(s => s.GetProperty("id").GetInt64()).ToList();
        Assert.Contains(0L, stores);                       // head office
        Assert.Contains(fx.Acme.FirstStoreId, stores);
        Assert.Equal(fx.Acme.FirstStoreId, byName.StoreId);   // defaults to the first selling store, not head office
    }

    [Fact]
    public async Task Wrong_password_unknown_user_and_unknown_company_all_look_the_same()
    {
        var a = await RawLogin("Acme Footwear", "owner", "Wrong-Pass-9");
        var b = await RawLogin("Acme Footwear", "nobody", "Whatever-9");
        var c = await RawLogin("No Such Company", "owner", ApiFixture.OwnerPassword);
        Assert.All([a, b, c], x => Assert.Equal(HttpStatusCode.Unauthorized, x.Item1));
        Assert.Equal(a.Item2.GetProperty("title").GetString(), b.Item2.GetProperty("title").GetString());
        Assert.Equal(a.Item2.GetProperty("title").GetString(), c.Item2.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_and_the_attempts_are_logged()
    {
        await fx.ExecAsync("select 1");
        var cid = fx.Acme.CompanyId;
        // a dedicated user so the shared owner is never locked
        var hasher = new EFootwear.Infrastructure.Security.Argon2PasswordHasher();
        await fx.ExecAsync(@"select set_config('app.company_id', @c, false);
            insert into sec.users (company_id, username, password_hash, full_name, role_code, must_change_password)
            values (@cid, 'locky', @h, 'Locky', 'CASHIER_SALESMAN', false)",
            ("c", cid.ToString()), ("cid", cid), ("h", hasher.Hash("Right-Pass-1")));

        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await RawLogin("Acme Footwear", "locky", "Bad-Pass-1")).Item1);
        var locked = await RawLogin("Acme Footwear", "locky", "Right-Pass-1");     // even the right password is refused now
        Assert.Equal(HttpStatusCode.Unauthorized, locked.Item1);
        Assert.Contains("Too many failed attempts", locked.Item2.GetProperty("title").GetString());

        var wrong = await fx.ScalarAsync<long>("select count(*) from sec.login_attempts where company_id = @c and username_entered = 'locky' and outcome = 'WRONG_PASSWORD'", ("c", cid));
        Assert.Equal(4, wrong);
        Assert.True(await fx.ScalarAsync<long>("select count(*) from sec.login_attempts where company_id = @c and username_entered = 'locky' and outcome = 'LOCKED'", ("c", cid)) >= 1);
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_bad_tokens_are_rejected()
    {
        var s = await fx.OwnerAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Get, "/api/v1/auth/me")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await s.PostAsync("/api/v1/auth/logout")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.SendAsync(HttpMethod.Get, "/api/v1/auth/me")).Status);

        using var anon = fx.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/auth/me")).StatusCode);
        anon.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", $"{fx.Acme.CompanyId}.{new string('a', 64)}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/auth/me")).StatusCode);
        anon.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "garbage");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_token_of_one_company_is_useless_for_another()
    {
        var s = await fx.OwnerAsync();
        var secret = s.Token[(s.Token.IndexOf('.') + 1)..];
        using var c = fx.NewClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", $"{fx.Globex.CompanyId}.{secret}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Cannot_select_a_store_you_are_not_assigned_to()
    {
        using var c = fx.NewClient();
        var s = await fx.LoginAsync("Acme Footwear", "owner", ApiFixture.OwnerPassword);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
        c.DefaultRequestHeaders.Add("X-Store-Id", fx.Globex.FirstStoreId.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Temporary_password_blocks_everything_until_it_is_changed()
    {
        var hasher = new EFootwear.Infrastructure.Security.Argon2PasswordHasher();
        var cid = fx.Acme.CompanyId;
        await fx.ExecAsync(@"select set_config('app.company_id', @c, false);
            insert into sec.users (company_id, username, password_hash, full_name, role_code, must_change_password)
            values (@cid, 'newbie', @h, 'Newbie', 'CASHIER_SALESMAN', true)", ("c", cid.ToString()), ("cid", cid), ("h", hasher.Hash("Temp-Pass-1")));

        var s = await fx.LoginAsync("Acme Footwear", "newbie", "Temp-Pass-1");
        Assert.True(s.Me.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await s.SendAsync(HttpMethod.Get, "/api/v1/stores")).Status);
        Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Get, "/api/v1/auth/me")).Status);

        var weak = await s.PostAsync("/api/v1/auth/change-password", new { currentPassword = "Temp-Pass-1", newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.Status);
        var bad = await s.PostAsync("/api/v1/auth/change-password", new { currentPassword = "nope-nope-1", newPassword = "Better-Pass-2" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal(HttpStatusCode.NoContent, (await s.PostAsync("/api/v1/auth/change-password", new { currentPassword = "Temp-Pass-1", newPassword = "Better-Pass-2" })).Status);

        // all sessions ended; the old password no longer works
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.SendAsync(HttpMethod.Get, "/api/v1/auth/me")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RawLogin("Acme Footwear", "newbie", "Temp-Pass-1")).Item1);
        var again = await fx.LoginAsync("Acme Footwear", "newbie", "Better-Pass-2");
        Assert.False(again.Me.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Row_level_security_hides_other_companies_even_with_a_hand_written_query()
    {
        // The API's own login role, scoped to Acme, asks for every user and every store in the database.
        await using var conn = new NpgsqlConnection(fx.ApiConnection);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await Run(conn, tx, "set local role pos_app");
        await Run(conn, tx, $"select set_config('app.company_id', '{fx.Acme.CompanyId}', true), set_config('app.all_stores', 'on', true)");
        Assert.Equal(await fx.ScalarAsync<long>("select count(*) from sec.users where company_id = @c", ("c", fx.Acme.CompanyId)), await Scalar(conn, tx, "select count(*) from sec.users"));   // only Acme's users, however many exist
        Assert.Equal(0L, await Scalar(conn, tx, $"select count(*) from core.stores where company_id = {fx.Globex.CompanyId}"));
        Assert.Equal(0L, await Scalar(conn, tx, $"select count(*) from acct.accounts where company_id = {fx.Globex.CompanyId}"));

        // and cannot write into another company
        var ex = await Assert.ThrowsAsync<PostgresException>(() => Run(conn, tx,
            $"insert into core.stores (company_id, code, name) values ({fx.Globex.CompanyId}, 'EVIL', 'Evil')"));
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact]
    public async Task The_operator_command_registers_a_company_whose_owner_must_change_the_first_password()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Admin", fx.OwnerConnection);
        Environment.SetEnvironmentVariable("OWNER_PASSWORD", "Cli-Owner-1");
        Environment.SetEnvironmentVariable("Database__ApiRole", "");   // never the developer's role from user-secrets
        try
        {
            Assert.Equal(0, await Cli.RunAsync(["migrate"]));   // already up to date
            Assert.Equal(0, await Cli.RunAsync(["create-company", "Cli Company", "C1", "Cli Store", "cliowner", "Cli Owner"]));
        }
        finally { Environment.SetEnvironmentVariable("ConnectionStrings__Admin", null); Environment.SetEnvironmentVariable("OWNER_PASSWORD", null); }

        var s = await fx.LoginAsync("Cli Company", "cliowner", "Cli-Owner-1");
        Assert.True(s.Me.GetProperty("mustChangePassword").GetBoolean());
        Assert.True(s.Me.GetProperty("isOwner").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await s.SendAsync(HttpMethod.Get, "/api/v1/stores")).Status);   // until the password is changed
        Assert.Equal(1, Cli.Usage.Contains("create-company") ? 1 : 0);
    }

    [Fact]
    public async Task Company_provisioning_creates_the_chart_head_office_and_store_accounts()
    {
        Assert.Equal(1L, await fx.ScalarAsync<long>("select count(*) from core.stores where company_id = @c and is_head_office", ("c", fx.Acme.CompanyId)));
        Assert.Equal(1L, await fx.ScalarAsync<long>("select count(*) from party.parties where company_id = @c and code = 'WALKIN'", ("c", fx.Acme.CompanyId)));
        Assert.Equal(6L, await fx.ScalarAsync<long>("select count(*) from acct.accounts where company_id = @c and store_id = @s", ("c", fx.Acme.CompanyId), ("s", fx.Acme.FirstStoreId)));
        Assert.Equal(1L, await fx.ScalarAsync<long>("select count(*) from sec.auth_policy where company_id = @c", ("c", fx.Acme.CompanyId)));
    }

    private static async Task Run(NpgsqlConnection c, NpgsqlTransaction t, string sql) { await using var cmd = new NpgsqlCommand(sql, c, t); await cmd.ExecuteNonQueryAsync(); }
    private static async Task<long> Scalar(NpgsqlConnection c, NpgsqlTransaction t, string sql) { await using var cmd = new NpgsqlCommand(sql, c, t); return (long)(await cmd.ExecuteScalarAsync())!; }
}
