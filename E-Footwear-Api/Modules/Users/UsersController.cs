using System.ComponentModel.DataAnnotations;
using Dapper;
using E_Footwear_Api.Infrastructure;
using E_Footwear_Api.Modules.Auth;
using E_Footwear_Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;

namespace E_Footwear_Api.Modules.Users;

public sealed class UserListItem
{
    public long Id { get; init; }
    public string UserName { get; init; } = "";
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public string Status { get; init; } = "";
    public bool IsAdministrator { get; init; }
    public bool IsCompanyOwner { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public long[] RoleIds { get; init; } = [];
    public long[] StoreIds { get; init; } = [];
}
public sealed record CreateUserRequest([Required] string UserName, string? DisplayName, string? Email, [Required] string Password,
    long[]? RoleIds, long[]? StoreIds, bool IsAdministrator, bool IsCompanyOwner,
    bool CanEdit, bool CanDelete, bool CanPrint, bool CanChangePrice, bool CanGiveDiscount);
public sealed record UpdateUserRequest(string? DisplayName, string? Email, bool IsAdministrator, bool IsCompanyOwner,
    bool CanEdit, bool CanDelete, bool CanPrint, bool CanChangePrice, bool CanGiveDiscount);
public sealed record ResetPasswordRequest([Required] string NewPassword);
public sealed record IdsRequest(long[] Ids);
public sealed record PermissionOverride(string Key, bool Allowed);
public sealed record OverridesRequest(PermissionOverride[] Overrides);

/// <summary>Phase 3 "Users" (Part 6). All writes are permission-checked, tenant-scoped (RLS) and audited.</summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController(IDb db, ICurrentUserAccessor accessor, IOptions<SecurityOptions> secOpt) : ControllerBase
{
    private const string Create = "UserAccount.CreateNewUser";

    [HttpGet, RequirePermission("UserAccount.ViewUser")]
    public async Task<IEnumerable<UserListItem>> List(CancellationToken ct)
    {
        var u = await accessor.GetAsync(HttpContext, ct);
        return await db.InTenantAsync(u.Scope, (c, t) => c.QueryAsync<UserListItem>(
            "select u.id, u.user_name as UserName, u.display_name as DisplayName, u.email, u.status, " +
            "u.is_administrator as IsAdministrator, u.is_company_owner as IsCompanyOwner, u.last_login_at as LastLoginAt, " +
            "coalesce((select array_agg(role_id) from security.user_roles r where r.user_id=u.id), '{}') as RoleIds, " +
            "coalesce((select array_agg(store_id) from security.user_stores s where s.user_id=u.id), '{}') as StoreIds " +
            "from security.users u where not u.is_deleted order by u.user_name", transaction: t), ct);
    }

    [HttpPost, RequirePermission(Create)]
    public async Task<ActionResult<UserListItem>> CreateUser(CreateUserRequest req, CancellationToken ct)
    {
        var me = await accessor.GetAsync(HttpContext, ct);
        RequirePasswordStrength(req.Password);
        EnsureMayGrantPrivilege(me, req.IsAdministrator || req.IsCompanyOwner);
        var storeIds = req.StoreIds ?? [];
        if (!me.AllStores && storeIds.Any(s => !me.StoreIds.Contains(s))) throw ApiException.Forbidden("Cannot assign a store outside your own access.");

        var id = await db.InTenantAsync(me.Scope, async (c, t) =>
        {
            var uid = await c.ExecuteScalarAsync<long>(
                "insert into security.users(company_id,user_name,display_name,email,password_hash,is_administrator,is_company_owner," +
                "can_edit,can_delete,can_print,can_change_price,can_give_discount,must_change_password,created_by) " +
                "values (@cid,@UserName,@DisplayName,@Email,@ph,@IsAdministrator,@IsCompanyOwner,@CanEdit,@CanDelete,@CanPrint,@CanChangePrice,@CanGiveDiscount,true,@by) returning id",
                new { cid = me.CompanyId, req.UserName, req.DisplayName, req.Email, ph = PasswordHasher.Hash(req.Password),
                      req.IsAdministrator, req.IsCompanyOwner, req.CanEdit, req.CanDelete, req.CanPrint, req.CanChangePrice, req.CanGiveDiscount, by = me.UserId }, t);
            await SetRoles(c, t, me.CompanyId, uid, req.RoleIds ?? []);
            await SetStores(c, t, me.CompanyId, uid, storeIds);
            await Audit.Log(c, t, me.CompanyId, me.UserId, me.UserName, "New User", "user", uid, remarks: req.UserName);
            return uid;
        }, ct);
        return Created($"/api/v1/users/{id}", await Get(me, id, ct));
    }

    [HttpPut("{id:long}"), RequirePermission(Create)]
    public async Task<UserListItem> Update(long id, UpdateUserRequest req, CancellationToken ct)
    {
        var me = await accessor.GetAsync(HttpContext, ct);
        await db.InTenantAsync(me.Scope, async (c, t) =>
        {
            var target = await LoadTarget(c, t, id);
            EnsureMayManage(me, target);
            EnsureMayGrantPrivilege(me, (req.IsAdministrator || req.IsCompanyOwner) != (target.IsAdministrator || target.IsCompanyOwner));
            if (id == me.UserId && (!req.IsAdministrator && target.IsAdministrator || !req.IsCompanyOwner && target.IsCompanyOwner))
                throw ApiException.BadRequest("self_demotion", "You cannot remove your own administrator/owner rights.");
            await c.ExecuteAsync(
                "update security.users set display_name=@DisplayName, email=@Email, is_administrator=@IsAdministrator, is_company_owner=@IsCompanyOwner, " +
                "can_edit=@CanEdit, can_delete=@CanDelete, can_print=@CanPrint, can_change_price=@CanChangePrice, can_give_discount=@CanGiveDiscount, " +
                "updated_at=now(), updated_by=@by where id=@id",
                new { req.DisplayName, req.Email, req.IsAdministrator, req.IsCompanyOwner, req.CanEdit, req.CanDelete, req.CanPrint,
                      req.CanChangePrice, req.CanGiveDiscount, by = me.UserId, id }, t);
            await Audit.Log(c, t, me.CompanyId, me.UserId, me.UserName, "Edit User", "user", id);
            return 0;
        }, ct);
        return await Get(me, id, ct);
    }

    [HttpPost("{id:long}/{verb:regex(^(block|activate)$)}"), RequirePermission(Create)]
    public async Task<IActionResult> SetStatus(long id, string verb, CancellationToken ct)
    {
        var me = await accessor.GetAsync(HttpContext, ct);
        if (id == me.UserId && verb == "block") throw ApiException.BadRequest("self_block", "You cannot block yourself.");
        await db.InTenantAsync(me.Scope, async (c, t) =>
        {
            EnsureMayManage(me, await LoadTarget(c, t, id));
            await c.ExecuteAsync("update security.users set status=@s, updated_at=now(), updated_by=@by where id=@id",
                new { s = verb == "block" ? "BLOCKED" : "ACTIVE", by = me.UserId, id }, t);
            if (verb == "block")
                await c.ExecuteAsync("update security.refresh_tokens set revoked_at=now() where user_id=@id and revoked_at is null", new { id }, t);
            await Audit.Log(c, t, me.CompanyId, me.UserId, me.UserName, verb == "block" ? "Block User" : "Activate User", "user", id);
            return 0;
        }, ct);
        return NoContent();
    }

    [HttpPost("{id:long}/reset-password"), RequirePermission(Create)]
    public async Task<IActionResult> ResetPassword(long id, ResetPasswordRequest req, CancellationToken ct)
    {
        var me = await accessor.GetAsync(HttpContext, ct);
        RequirePasswordStrength(req.NewPassword);
        await db.InTenantAsync(me.Scope, async (c, t) =>
        {
            EnsureMayManage(me, await LoadTarget(c, t, id));
            await c.ExecuteAsync("update security.users set password_hash=@h, must_change_password=true, failed_login_count=0, lockout_end=null, updated_at=now(), updated_by=@by where id=@id",
                new { h = PasswordHasher.Hash(req.NewPassword), by = me.UserId, id }, t);
            await c.ExecuteAsync("update security.refresh_tokens set revoked_at=now() where user_id=@id and revoked_at is null", new { id }, t);
            await Audit.Log(c, t, me.CompanyId, me.UserId, me.UserName, "Administrator Password Reset", "user", id);
            return 0;
        }, ct);
        return NoContent();
    }

    [HttpPut("{id:long}/roles"), RequirePermission(Create)]
    public async Task<IActionResult> PutRoles(long id, IdsRequest req, CancellationToken ct)
    {
        var me = await accessor.GetAsync(HttpContext, ct);
        await db.InTenantAsync(me.Scope, async (c, t) =>
        {
            EnsureMayManage(me, await LoadTarget(c, t, id));
            await SetRoles(c, t, me.CompanyId, id, req.Ids);
            await Audit.Log(c, t, me.CompanyId, me.UserId, me.UserName, "Set User Roles", "user", id);
            return 0;
        }, ct);
        return NoContent();
    }

    [HttpPut("{id:long}/stores"), RequirePermission(Create)]
    public async Task<IActionResult> PutStores(long id, IdsRequest req, CancellationToken ct)
    {
        var me = await accessor.GetAsync(HttpContext, ct);
        if (!me.AllStores && req.Ids.Any(s => !me.StoreIds.Contains(s))) throw ApiException.Forbidden("Cannot assign a store outside your own access.");
        await db.InTenantAsync(me.Scope, async (c, t) =>
        {
            EnsureMayManage(me, await LoadTarget(c, t, id));
            await SetStores(c, t, me.CompanyId, id, req.Ids);
            await Audit.Log(c, t, me.CompanyId, me.UserId, me.UserName, "Set User Stores", "user", id);
            return 0;
        }, ct);
        return NoContent();
    }

    /// <summary>Per-user overrides on top of the role template (Part 6): replaces the full override set.</summary>
    [HttpPut("{id:long}/permissions"), RequirePermission(Create)]
    public async Task<IActionResult> PutOverrides(long id, OverridesRequest req, CancellationToken ct)
    {
        var me = await accessor.GetAsync(HttpContext, ct);
        // A manager may only grant what they themselves hold (no privilege escalation through overrides).
        if (req.Overrides.Any(o => o.Allowed && !me.Has(o.Key))) throw ApiException.Forbidden("Cannot grant a permission you do not hold.");
        await db.InTenantAsync(me.Scope, async (c, t) =>
        {
            EnsureMayManage(me, await LoadTarget(c, t, id));
            await c.ExecuteAsync("delete from security.user_permissions where user_id=@id", new { id }, t);
            foreach (var o in req.Overrides.DistinctBy(x => x.Key))
                await c.ExecuteAsync("insert into security.user_permissions(company_id,user_id,permission_key,allowed) values (@cid,@id,@k,@a)",
                    new { cid = me.CompanyId, id, k = o.Key, a = o.Allowed }, t);
            await Audit.Log(c, t, me.CompanyId, me.UserId, me.UserName, "Set User Permissions", "user", id);
            return 0;
        }, ct);
        return NoContent();
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private sealed record Target(long Id, bool IsAdministrator, bool IsCompanyOwner);

    private static async Task<Target> LoadTarget(NpgsqlConnection c, NpgsqlTransaction t, long id) =>
        await c.QuerySingleOrDefaultAsync<Target>(
            "select id, is_administrator as IsAdministrator, is_company_owner as IsCompanyOwner from security.users where id=@id and not is_deleted", new { id }, t)
        ?? throw ApiException.NotFound("User not found.");

    private static void EnsureMayManage(CurrentUser me, Target target)
    {
        if ((target.IsAdministrator || target.IsCompanyOwner) && !(me.IsAdministrator || me.IsCompanyOwner))
            throw ApiException.Forbidden("Only an administrator or owner can manage this user.");
    }

    private static void EnsureMayGrantPrivilege(CurrentUser me, bool privileged)
    {
        if (privileged && !(me.IsAdministrator || me.IsCompanyOwner)) throw ApiException.Forbidden("Only an administrator or owner can grant administrator/owner rights.");
    }

    private void RequirePasswordStrength(string pw)
    {
        var min = secOpt.Value.MinPasswordLength;
        if (pw.Length < min) throw ApiException.BadRequest("weak_password", $"Password must be at least {min} characters.");
    }

    private static async Task SetRoles(NpgsqlConnection c, NpgsqlTransaction t, long cid, long userId, long[] roleIds)
    {
        await c.ExecuteAsync("delete from security.user_roles where user_id=@userId", new { userId }, t);
        foreach (var r in roleIds.Distinct())
            await c.ExecuteAsync("insert into security.user_roles(company_id,user_id,role_id) values (@cid,@userId,@r)", new { cid, userId, r }, t);
    }

    private static async Task SetStores(NpgsqlConnection c, NpgsqlTransaction t, long cid, long userId, long[] storeIds)
    {
        await c.ExecuteAsync("delete from security.user_stores where user_id=@userId", new { userId }, t);
        var first = true;
        foreach (var s in storeIds.Distinct())
        {
            await c.ExecuteAsync("insert into security.user_stores(company_id,user_id,store_id,is_default) values (@cid,@userId,@s,@d)", new { cid, userId, s, d = first }, t);
            first = false;
        }
    }

    private async Task<UserListItem> Get(CurrentUser me, long id, CancellationToken ct) =>
        (await db.InTenantAsync(me.Scope, (c, t) => c.QueryAsync<UserListItem>(
            "select u.id, u.user_name as UserName, u.display_name as DisplayName, u.email, u.status, " +
            "u.is_administrator as IsAdministrator, u.is_company_owner as IsCompanyOwner, u.last_login_at as LastLoginAt, " +
            "coalesce((select array_agg(role_id) from security.user_roles r where r.user_id=u.id), '{}') as RoleIds, " +
            "coalesce((select array_agg(store_id) from security.user_stores s where s.user_id=u.id), '{}') as StoreIds " +
            "from security.users u where u.id=@id and not u.is_deleted", new { id }, t), ct)).Single();
}
