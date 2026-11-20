using System.Net;
using EFootwear.Api.Tests.Support;
using Xunit;

namespace EFootwear.Api.Tests;

[Collection("api")]
public sealed class AdminTests(ApiFixture fx)
{
    private static string U(string p) => p + Guid.NewGuid().ToString("N")[..6];

    private async Task<(Session s, long id)> NewUserAsync(Session owner, string role, string[] perms, long[]? stores = null, bool all = false, string pw = "Start-Pass-1")
    {
        var name = U("u");
        var (st, body) = await owner.PostAsync("/api/v1/users", new { userName = name, fullName = name, role, password = pw, allStores = all, storeIds = stores ?? [fx.Acme.FirstStoreId], permissions = perms });
        Assert.Equal(HttpStatusCode.Created, st);
        var id = body.GetProperty("id").GetInt64();
        var s = await fx.LoginAsync("Acme Footwear", name, pw);
        return (s, id);
    }

    [Fact]
    public async Task Created_user_starts_with_a_temporary_password_and_only_the_granted_permissions()
    {
        var owner = await fx.OwnerAsync();
        var (s, _) = await NewUserAsync(owner, "CASHIER_SALESMAN", ["SALE_CREATE"]);
        Assert.True(s.Me.GetProperty("mustChangePassword").GetBoolean());
        var perms = s.Me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Equal(["SALE_CREATE"], perms);
        Assert.False(s.Me.GetProperty("isOwner").GetBoolean());
    }

    [Fact]
    public async Task A_cashier_cannot_administer_users_or_company_settings()
    {
        var owner = await fx.OwnerAsync();
        var (cashier, _) = await NewUserAsync(owner, "CASHIER_SALESMAN", ["SALE_CREATE"]);
        await cashier.PostAsync("/api/v1/auth/change-password", new { currentPassword = "Start-Pass-1", newPassword = "Next-Pass-22" });
        var c = await fx.LoginAsync("Acme Footwear", cashier.Me.GetProperty("userName").GetString()!, "Next-Pass-22");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(HttpMethod.Get, "/api/v1/users")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsync("/api/v1/company", new { name = "Hacked" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/v1/masters/brands", new { name = "Nike" })).Status);
    }

    [Fact]
    public async Task Nobody_can_grant_more_than_they_hold_or_create_an_owner()
    {
        var owner = await fx.OwnerAsync();
        var (admin, _) = await NewUserAsync(owner, "STORE_MANAGER", ["USER_ADMIN", "SALE_CREATE"]);
        await admin.PostAsync("/api/v1/auth/change-password", new { currentPassword = "Start-Pass-1", newPassword = "Next-Pass-22" });
        admin = await fx.LoginAsync("Acme Footwear", admin.Me.GetProperty("userName").GetString()!, "Next-Pass-22");

        var n = U("e");
        var grant = await admin.PostAsync("/api/v1/users", new { userName = n, fullName = n, role = "CASHIER_SALESMAN", password = "Start-Pass-1", storeIds = new[] { fx.Acme.FirstStoreId }, permissions = new[] { "SALE_CREATE", "VOUCHER_POST" } });
        Assert.Equal(HttpStatusCode.Forbidden, grant.Status);
        var owner2 = await admin.PostAsync("/api/v1/users", new { userName = U("o"), fullName = "O", role = "COMPANY_OWNER", password = "Start-Pass-1", allStores = true });
        Assert.Equal(HttpStatusCode.Forbidden, owner2.Status);
        var allStores = await admin.PostAsync("/api/v1/users", new { userName = U("a"), fullName = "A", role = "CASHIER_SALESMAN", password = "Start-Pass-1", allStores = true, permissions = new[] { "SALE_CREATE" } });
        Assert.Equal(HttpStatusCode.Forbidden, allStores.Status);
        var ok = await admin.PostAsync("/api/v1/users", new { userName = n, fullName = n, role = "CASHIER_SALESMAN", password = "Start-Pass-1", storeIds = new[] { fx.Acme.FirstStoreId }, permissions = new[] { "SALE_CREATE" } });
        Assert.Equal(HttpStatusCode.Created, ok.Status);

        // an admin cannot touch an owner, nor change their own permissions
        var ownerId = owner.UserId;
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync($"/api/v1/users/{ownerId}", new { fullName = "x", role = "CASHIER_SALESMAN", storeIds = new[] { fx.Acme.FirstStoreId } })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync($"/api/v1/users/{admin.UserId}", new { fullName = "x", role = "STORE_MANAGER", storeIds = new[] { fx.Acme.FirstStoreId }, permissions = new[] { "USER_ADMIN" } })).Status);
    }

    [Fact]
    public async Task Blocking_a_user_takes_effect_on_the_very_next_request_and_unblocking_restores_login()
    {
        var owner = await fx.OwnerAsync();
        var (s, id) = await NewUserAsync(owner, "CASHIER_SALESMAN", ["SALE_CREATE"]);
        Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Get, "/api/v1/auth/me")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/v1/users/{id}/status", new { status = "DISABLED" })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.SendAsync(HttpMethod.Get, "/api/v1/auth/me")).Status);
        await Assert.ThrowsAsync<HttpRequestException>(() => fx.LoginAsync("Acme Footwear", s.Me.GetProperty("userName").GetString()!, "Start-Pass-1"));
        await owner.PostAsync($"/api/v1/users/{id}/status", new { status = "ACTIVE" });
        await fx.LoginAsync("Acme Footwear", s.Me.GetProperty("userName").GetString()!, "Start-Pass-1");
        // nobody blocks themselves, and the last owner stays active
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsync($"/api/v1/users/{owner.UserId}/status", new { status = "DISABLED" })).Status);
    }

    [Fact]
    public async Task Removing_a_permission_applies_immediately_and_admin_reset_forces_a_new_password()
    {
        var owner = await fx.OwnerAsync();
        var (s, id) = await NewUserAsync(owner, "STORE_MANAGER", ["MASTER_EDIT"]);
        await s.PostAsync("/api/v1/auth/change-password", new { currentPassword = "Start-Pass-1", newPassword = "Next-Pass-22" });
        s = await fx.LoginAsync("Acme Footwear", s.Me.GetProperty("userName").GetString()!, "Next-Pass-22");
        Assert.Equal(HttpStatusCode.Created, (await s.PostAsync("/api/v1/masters/brands", new { name = U("Brand") })).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync($"/api/v1/users/{id}", new { fullName = "Mgr", role = "STORE_MANAGER", storeIds = new[] { fx.Acme.FirstStoreId }, permissions = Array.Empty<string>() })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.PostAsync("/api/v1/masters/brands", new { name = U("Brand") })).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/v1/users/{id}/reset-password", new { temporaryPassword = "Temp-Pass-77" })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.SendAsync(HttpMethod.Get, "/api/v1/auth/me")).Status);   // sessions ended
        var again = await fx.LoginAsync("Acme Footwear", s.Me.GetProperty("userName").GetString()!, "Temp-Pass-77");
        Assert.True(again.Me.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Opening_a_store_creates_its_own_accounts_and_users_only_see_their_stores()
    {
        var owner = await fx.OwnerAsync();
        var code = U("S").ToUpperInvariant();
        var (st, body) = await owner.PostAsync("/api/v1/stores", new { code, name = "Branch " + code });
        Assert.Equal(HttpStatusCode.Created, st);
        var storeId = body.GetProperty("id").GetInt64();
        Assert.Equal(6L, await fx.ScalarAsync<long>("select count(*) from acct.accounts where company_id = @c and store_id = @s", ("c", fx.Acme.CompanyId), ("s", storeId)));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync("/api/v1/stores", new { code, name = "Dup" })).Status);

        var (cashier, _) = await NewUserAsync(owner, "CASHIER_SALESMAN", ["SALE_CREATE"]);
        await cashier.PostAsync("/api/v1/auth/change-password", new { currentPassword = "Start-Pass-1", newPassword = "Next-Pass-22" });
        cashier = await fx.LoginAsync("Acme Footwear", cashier.Me.GetProperty("userName").GetString()!, "Next-Pass-22");
        var visible = (await cashier.GetJsonAsync("/api/v1/stores")).EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToList();
        Assert.Equal([fx.Acme.FirstStoreId], visible);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.SendAsync(HttpMethod.Get, $"/api/v1/stores/{storeId}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync("/api/v1/stores", new { code = U("X"), name = "No" })).Status);
    }

    [Fact]
    public async Task Master_lists_crud_duplicates_and_near_duplicate_cities()
    {
        var owner = await fx.OwnerAsync();
        var name = U("Brand");
        var (st, b) = await owner.PostAsync("/api/v1/masters/brands", new { name, nameUr = "برانڈ", sortOrder = 2 });
        Assert.Equal(HttpStatusCode.Created, st);
        var id = b.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync("/api/v1/masters/brands", new { name = name.ToUpperInvariant() })).Status);   // case-insensitive unique
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/v1/masters/brands", new { name = " " })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.SendAsync(HttpMethod.Get, "/api/v1/masters/nonsense")).Status);

        var list = await owner.GetJsonAsync("/api/v1/masters/brands?search=" + name);
        Assert.Equal(1, list.GetArrayLength());
        var rowVer = list[0].GetProperty("rowVer").GetInt64();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync($"/api/v1/masters/brands/{id}", new { name = name + "X", sortOrder = 3, isActive = true, rowVer })).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsync($"/api/v1/masters/brands/{id}", new { name = name + "Y", sortOrder = 3, isActive = true, rowVer })).Status);   // stale row version
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/masters/brands/{id}")).Status);
        Assert.Equal(0, (await owner.GetJsonAsync("/api/v1/masters/brands?search=" + name)).GetArrayLength());
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsync("/api/v1/masters/brands", new { name = name + "X" })).Status);   // the name is free again

        var city = U("Sheikhupura");
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsync("/api/v1/masters/cities", new { name = city })).Status);
        var typo = city.Replace("Sheikh", "Shiekh");
        var near = await owner.PostAsync("/api/v1/masters/cities", new { name = typo });
        Assert.Equal(HttpStatusCode.Conflict, near.Status);
        Assert.Equal("similar_city", near.Body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Categories_sizes_and_tenant_isolation_of_master_data()
    {
        var owner = await fx.OwnerAsync();
        var (_, dep) = await owner.PostAsync("/api/v1/masters/departments", new { name = U("Footwear") });
        var depId = dep.GetProperty("id").GetInt64();
        var (cs, cat) = await owner.PostAsync("/api/v1/masters/categories", new { name = "Sandals", departmentId = depId });
        Assert.Equal(HttpStatusCode.Created, cs);
        var catId = cat.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsync($"/api/v1/masters/categories/{catId}", new { name = "Sandals", departmentId = depId, parentId = catId })).Status);

        var (_, grp) = await owner.PostAsync("/api/v1/masters/size-groups", new { name = U("Adult") });
        var grpId = grp.GetProperty("id").GetInt64();
        foreach (var sz in new[] { "6", "7", "8" }) Assert.Equal(HttpStatusCode.Created, (await owner.PostAsync("/api/v1/masters/sizes", new { sizeGroupId = grpId, sizeCode = sz, sortOrder = int.Parse(sz) })).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync("/api/v1/masters/sizes", new { sizeGroupId = grpId, sizeCode = "7" })).Status);
        Assert.Equal(3, (await owner.GetJsonAsync($"/api/v1/masters/sizes?sizeGroupId={grpId}")).GetArrayLength());

        // Globex sees none of Acme's lists
        var globex = await fx.LoginAsync("Globex Shoes", "owner", ApiFixture.OwnerPassword);
        Assert.Equal(0, (await globex.GetJsonAsync("/api/v1/masters/categories")).GetArrayLength());
        Assert.Equal(0, (await globex.GetJsonAsync($"/api/v1/masters/sizes?sizeGroupId={grpId}")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await globex.PostAsync("/api/v1/masters/categories", new { name = "Sandals", departmentId = depId })).Status);   // Acme's department id is not visible to Globex
    }

    [Fact]
    public async Task Parties_validation_roles_codes_and_the_built_in_cash_sale_customer()
    {
        var owner = await fx.OwnerAsync();
        var (st, c) = await owner.PostAsync("/api/v1/parties", new { name = "Ali Khan", isCustomer = true, cnic = "35202-1234567-1", phonePrimary = "0300-1111111" });
        Assert.Equal(HttpStatusCode.Created, st);
        var id = c.GetProperty("id").GetInt64();
        var got = await owner.GetJsonAsync($"/api/v1/parties/{id}");
        Assert.StartsWith("C", got.GetProperty("code").GetString());
        Assert.Equal(0, got.GetProperty("balance").GetInt64());

        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync("/api/v1/parties", new { name = "Dup", isCustomer = true, cnic = "35202-1234567-1" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/v1/parties", new { name = "Bad", isCustomer = true, cnic = "123" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/v1/parties", new { name = "NoRole" })).Status);

        var (vs, v) = await owner.PostAsync("/api/v1/parties", new { name = "Bata Supplier", isVendor = true, creditLimit = 500000 });
        Assert.Equal(HttpStatusCode.Created, vs);
        Assert.StartsWith("V", (await owner.GetJsonAsync($"/api/v1/parties/{v.GetProperty("id").GetInt64()}")).GetProperty("code").GetString());

        var walkin = (await owner.GetJsonAsync("/api/v1/parties?search=Cash Sale")).GetProperty("items")[0];
        Assert.Equal("WALKIN", walkin.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.DeleteAsync($"/api/v1/parties/{walkin.GetProperty("id").GetInt64()}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/parties/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.SendAsync(HttpMethod.Get, $"/api/v1/parties/{id}")).Status);

        // other companies cannot see or reuse it
        var globex = await fx.LoginAsync("Globex Shoes", "owner", ApiFixture.OwnerPassword);
        Assert.Equal(HttpStatusCode.NotFound, (await globex.SendAsync(HttpMethod.Get, $"/api/v1/parties/{v.GetProperty("id").GetInt64()}")).Status);
    }

    [Fact]
    public async Task Staff_profile_and_store_transfer_are_recorded_with_history()
    {
        var owner = await fx.OwnerAsync();
        var (_, b) = await owner.PostAsync("/api/v1/stores", new { code = U("T").ToUpperInvariant(), name = "Second" });
        var second = b.GetProperty("id").GetInt64();
        var (st, e) = await owner.PostAsync("/api/v1/parties", new { name = "Bilal Salesman", isStaff = true, staff = new { designation = "Salesman", baseSalary = 30000, storeId = fx.Acme.FirstStoreId } });
        Assert.Equal(HttpStatusCode.Created, st);
        var id = e.GetProperty("id").GetInt64();
        Assert.Equal(30000, (await owner.GetJsonAsync($"/api/v1/parties/{id}")).GetProperty("staff").GetProperty("baseSalary").GetInt64());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/v1/parties/{id}/move-staff", new { storeId = second, effectiveOn = "2026-11-01" })).Status);
        var hist = await owner.GetJsonAsync($"/api/v1/parties/{id}/staff-history");
        Assert.Equal(1, hist.GetArrayLength());
        Assert.Equal(second, (await owner.GetJsonAsync($"/api/v1/parties/{id}")).GetProperty("staff").GetProperty("currentStoreId").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/v1/parties", new { name = "NoStaffData", isStaff = true })).Status);
    }
}
