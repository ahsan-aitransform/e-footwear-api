using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using E_Footwear_Api.Security;

namespace E_Footwear_Api.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_verifies_and_is_salted()
    {
        var a = PasswordHasher.Hash("correct horse battery");
        var b = PasswordHasher.Hash("correct horse battery");
        Assert.NotEqual(a, b);
        Assert.StartsWith("argon2id$", a);
        Assert.True(PasswordHasher.Verify("correct horse battery", a));
        Assert.False(PasswordHasher.Verify("wrong", a));
        Assert.False(PasswordHasher.Verify("x", "not-a-hash"));
    }
}

public class FoundationTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _f;
    public FoundationTests(ApiFixture f) => _f = f;

    [Fact]
    public async Task Register_company_then_owner_logs_in_and_sees_stores_and_all_permissions()
    {
        var platform = await _f.PlatformClient();
        var co = await _f.RegisterCompany(platform);
        var (client, session) = await _f.Login(co.Code, co.OwnerName, co.OwnerPassword);

        var me = session.GetProperty("me");
        Assert.True(me.GetProperty("isCompanyOwner").GetBoolean());
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(1, me.GetProperty("stores").GetArrayLength());
        Assert.True(me.GetProperty("permissions").GetArrayLength() > 100);
        Assert.False(session.GetRawText().Contains("passwordHash", StringComparison.OrdinalIgnoreCase));

        var me2 = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, me2.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_401_and_account_locks_after_configured_failures()
    {
        var platform = await _f.PlatformClient();
        var co = await _f.RegisterCompany(platform);
        var c = _f.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var r = await c.PostAsJsonAsync("/api/v1/auth/login", new { companyCode = co.Code, userName = co.OwnerName, password = "bad-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
        // now locked: even the right password is refused
        var locked = await c.PostAsJsonAsync("/api/v1/auth/login", new { companyCode = co.Code, userName = co.OwnerName, password = co.OwnerPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
    }

    [Fact]
    public async Task Tenant_isolation_company_B_cannot_see_or_touch_company_A_data()
    {
        var platform = await _f.PlatformClient();
        var a = await _f.RegisterCompany(platform);
        var b = await _f.RegisterCompany(platform);
        var (clientA, _) = await _f.Login(a.Code, a.OwnerName, a.OwnerPassword);
        var (clientB, _) = await _f.Login(b.Code, b.OwnerName, b.OwnerPassword);

        var s = await clientA.PostAsJsonAsync("/api/v1/stores", new { code = "SECRET", name = "A Secret Store" });
        Assert.Equal(HttpStatusCode.Created, s.StatusCode);
        var storeA = (await s.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var listB = await clientB.GetStringAsync("/api/v1/stores");
        Assert.DoesNotContain("A Secret Store", listB);

        // B guessing A's ids by URL: store update and user block are both "not found", never executed
        var put = await clientB.PutAsJsonAsync($"/api/v1/stores/{storeA}", new { name = "hijacked" });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        var block = await clientB.PostAsync($"/api/v1/users/{a.OwnerId}/block", null);
        Assert.Equal(HttpStatusCode.NotFound, block.StatusCode);

        var usersB = await clientB.GetStringAsync("/api/v1/users");
        Assert.DoesNotContain($"\"id\":{a.OwnerId},", usersB);

        // A's store is untouched
        var listA = await clientA.GetStringAsync("/api/v1/stores");
        Assert.Contains("A Secret Store", listA);
        Assert.DoesNotContain("hijacked", listA);
    }

    [Fact]
    public async Task Backend_enforces_permissions_roles_and_user_overrides()
    {
        var platform = await _f.PlatformClient();
        var co = await _f.RegisterCompany(platform);
        var (owner, _) = await _f.Login(co.Code, co.OwnerName, co.OwnerPassword);

        var roleResp = await owner.PostAsJsonAsync("/api/v1/roles", new { name = "Viewer" });
        var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var created = await owner.PostAsJsonAsync("/api/v1/users", new
        {
            userName = "cashier1", password = "cashier-pass-123", roleIds = new[] { roleId }, storeIds = new[] { co.StoreId }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var uid = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var (cashier, _) = await _f.Login(co.Code, "cashier1", "cashier-pass-123");
        // no permission yet, even though the endpoint exists
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/users")).StatusCode);

        // grant through the role template
        var put = await owner.PutAsJsonAsync($"/api/v1/roles/{roleId}/permissions", new { keys = new[] { "UserAccount.ViewUser" } });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/users")).StatusCode);

        // cashier still cannot create users (not granted)
        var denied = await cashier.PostAsJsonAsync("/api/v1/users", new { userName = "x", password = "some-password-1" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // per-user override beats the role template
        var ov = await owner.PutAsJsonAsync($"/api/v1/users/{uid}/permissions",
            new { overrides = new[] { new { key = "UserAccount.ViewUser", allowed = false } } });
        Assert.Equal(HttpStatusCode.NoContent, ov.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task Non_owner_cannot_escalate_privileges()
    {
        var platform = await _f.PlatformClient();
        var co = await _f.RegisterCompany(platform);
        var (owner, _) = await _f.Login(co.Code, co.OwnerName, co.OwnerPassword);
        var role = (await (await owner.PostAsJsonAsync("/api/v1/roles", new { name = "UserAdmin" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        await owner.PutAsJsonAsync($"/api/v1/roles/{role}/permissions", new { keys = new[] { "UserAccount.ViewUser", "UserAccount.CreateNewUser" } });
        await owner.PostAsJsonAsync("/api/v1/users", new { userName = "mgr", password = "manager-pass-123", roleIds = new[] { role }, storeIds = new[] { co.StoreId } });
        var (mgr, _) = await _f.Login(co.Code, "mgr", "manager-pass-123");

        var admin = await mgr.PostAsJsonAsync("/api/v1/users", new { userName = "evil", password = "evil-password-1", isAdministrator = true });
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        var block = await mgr.PostAsync($"/api/v1/users/{co.OwnerId}/block", null);
        Assert.Equal(HttpStatusCode.Forbidden, block.StatusCode);
        var grant = await mgr.PutAsJsonAsync($"/api/v1/roles/{role}/permissions", new { keys = new[] { "Settings.Stores" } });
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
    }

    [Fact]
    public async Task Blocking_a_user_takes_effect_immediately_on_existing_token()
    {
        var platform = await _f.PlatformClient();
        var co = await _f.RegisterCompany(platform);
        var (owner, _) = await _f.Login(co.Code, co.OwnerName, co.OwnerPassword);
        var created = await owner.PostAsJsonAsync("/api/v1/users", new { userName = "temp", password = "temp-password-1", storeIds = new[] { co.StoreId } });
        var uid = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var (temp, _) = await _f.Login(co.Code, "temp", "temp-password-1");
        Assert.Equal(HttpStatusCode.OK, (await temp.GetAsync("/api/v1/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/v1/users/{uid}/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await temp.GetAsync("/api/v1/me")).StatusCode);

        var relogin = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { companyCode = co.Code, userName = "temp", password = "temp-password-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_of_old_token_ends_all_sessions()
    {
        var platform = await _f.PlatformClient();
        var co = await _f.RegisterCompany(platform);
        var (_, session) = await _f.Login(co.Code, co.OwnerName, co.OwnerPassword);
        var first = session.GetProperty("refreshToken").GetString();
        var c = _f.CreateClient();

        var r1 = await c.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first });
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var second = (await r1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString();
        Assert.NotEqual(first, second);

        // replaying the rotated token is treated as theft
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first })).StatusCode);
        // ... and the newer token is dead too
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = second })).StatusCode);
    }

    [Fact]
    public async Task Store_assignment_is_enforced_at_login_and_in_listings()
    {
        var platform = await _f.PlatformClient();
        var co = await _f.RegisterCompany(platform);
        var (owner, _) = await _f.Login(co.Code, co.OwnerName, co.OwnerPassword);
        var s2 = (await (await owner.PostAsJsonAsync("/api/v1/stores", new { code = "S2", name = "Second" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var role = (await (await owner.PostAsJsonAsync("/api/v1/roles", new { name = "StoreView" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        await owner.PutAsJsonAsync($"/api/v1/roles/{role}/permissions", new { keys = new[] { "Settings.Stores" } });
        await owner.PostAsJsonAsync("/api/v1/users", new { userName = "s1user", password = "store1-pass-123", roleIds = new[] { role }, storeIds = new[] { co.StoreId } });

        var bad = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { companyCode = co.Code, userName = "s1user", password = "store1-pass-123", storeId = s2 });
        Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);

        var (u, _) = await _f.Login(co.Code, "s1user", "store1-pass-123");
        var list = await u.GetStringAsync("/api/v1/stores");
        Assert.Contains("Main Store", list);
        Assert.DoesNotContain("Second", list);
    }

    [Fact]
    public async Task Platform_endpoints_reject_anonymous_and_tenant_tokens()
    {
        var platform = await _f.PlatformClient();
        var co = await _f.RegisterCompany(platform);
        var (owner, _) = await _f.Login(co.Code, co.OwnerName, co.OwnerPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/v1/platform/companies")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/platform/companies")).StatusCode);
        // a platform token has no company context, so tenant endpoints refuse it
        Assert.Equal(HttpStatusCode.Unauthorized, (await platform.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Change_password_requires_current_password_and_ends_old_sessions()
    {
        var platform = await _f.PlatformClient();
        var co = await _f.RegisterCompany(platform);
        var (owner, session) = await _f.Login(co.Code, co.OwnerName, co.OwnerPassword);

        var wrong = await owner.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = "nope", newPassword = "brand-new-pass-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var weak = await owner.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = co.OwnerPassword, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var ok = await owner.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = co.OwnerPassword, newPassword = "brand-new-pass-1" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var oldRefresh = await _f.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        var (_, s2) = await _f.Login(co.Code, co.OwnerName, "brand-new-pass-1");
        Assert.False(s2.GetProperty("me").GetProperty("mustChangePassword").GetBoolean());
    }
}
