using Dapper;
using E_Footwear_Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace E_Footwear_Api.Security;

public sealed record CurrentUser(
    long UserId, long CompanyId, string UserName, bool IsAdministrator, bool IsCompanyOwner,
    IReadOnlyList<long> StoreIds, bool AllStores, IReadOnlySet<string> Permissions,
    bool CanEdit, bool CanDelete, bool CanPrint, bool CanChangePrice, bool CanGiveDiscount, bool MustChangePassword)
{
    public TenantScope Scope => new(CompanyId, StoreIds, AllStores);
    public bool Has(string key) => IsAdministrator || IsCompanyOwner || Permissions.Contains(key);
}

public interface ICurrentUserAccessor
{
    Task<CurrentUser> GetAsync(HttpContext ctx, CancellationToken ct = default);
}

/// <summary>
/// Loads the authenticated user's status, stores and effective permissions from the database on every request,
/// so blocking a user or changing permissions takes effect immediately (Part 6). Cached per request only.
/// </summary>
public sealed class CurrentUserAccessor(IDb db) : ICurrentUserAccessor
{
    private const string Key = "efw.currentUser";

    public async Task<CurrentUser> GetAsync(HttpContext ctx, CancellationToken ct = default)
    {
        if (ctx.Items[Key] is CurrentUser cached) return cached;
        var principal = ctx.User;
        if (principal.FindFirst(Claims.Kind)?.Value != "tenant"
            || !long.TryParse(principal.FindFirst(Claims.UserId)?.Value, out var uid)
            || !long.TryParse(principal.FindFirst(Claims.CompanyId)?.Value, out var cid))
            throw ApiException.Unauthorized("Not a company user token.");

        var user = await db.InSystemAsync(async (c, t) =>
        {
            var u = await c.QuerySingleOrDefaultAsync<UserRow>(
                "select id, user_name as UserName, status, is_administrator as IsAdministrator, is_company_owner as IsCompanyOwner, " +
                "can_edit as CanEdit, can_delete as CanDelete, can_print as CanPrint, can_change_price as CanChangePrice, " +
                "can_give_discount as CanGiveDiscount, must_change_password as MustChangePassword " +
                "from security.users where id=@uid and company_id=@cid and not is_deleted", new { uid, cid }, t);
            if (u is null || u.Status != "ACTIVE") return null;

            var all = u.IsAdministrator || u.IsCompanyOwner;
            var stores = all
                ? (await c.QueryAsync<long>("select id from core.stores where company_id=@cid and not is_deleted and is_active", new { cid }, t)).ToList()
                : (await c.QueryAsync<long>("select us.store_id from security.user_stores us join core.stores s on s.id=us.store_id " +
                    "where us.user_id=@uid and us.company_id=@cid and not s.is_deleted and s.is_active", new { uid, cid }, t)).ToList();

            var perms = new HashSet<string>(await c.QueryAsync<string>(
                "select distinct rp.permission_key from security.user_roles ur join security.role_permissions rp on rp.role_id=ur.role_id " +
                "join security.roles r on r.id=ur.role_id and not r.is_deleted where ur.user_id=@uid and ur.company_id=@cid", new { uid, cid }, t));
            foreach (var o in await c.QueryAsync<PermOverride>(
                "select permission_key as Key, allowed from security.user_permissions where user_id=@uid and company_id=@cid", new { uid, cid }, t))
            { if (o.Allowed) perms.Add(o.Key); else perms.Remove(o.Key); }

            return new CurrentUser(uid, cid, u.UserName, u.IsAdministrator, u.IsCompanyOwner, stores, all, perms,
                u.CanEdit, u.CanDelete, u.CanPrint, u.CanChangePrice, u.CanGiveDiscount, u.MustChangePassword);
        }, ct) ?? throw ApiException.Unauthorized("User is not active.");

        ctx.Items[Key] = user;
        return user;
    }

    private sealed record PermOverride(string Key, bool Allowed);

    private sealed record UserRow(long Id, string UserName, string Status, bool IsAdministrator, bool IsCompanyOwner,
        bool CanEdit, bool CanDelete, bool CanPrint, bool CanChangePrice, bool CanGiveDiscount, bool MustChangePassword);
}

/// <summary>Backend permission check (Part 6): enforced regardless of what the UI hides.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequirePermissionAttribute(string key) : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var accessor = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>();
        var user = await accessor.GetAsync(context.HttpContext);
        if (!user.Has(key)) throw ApiException.Forbidden();
    }
}

/// <summary>Platform admin endpoints: token must be of kind "platform".</summary>
public sealed class PlatformOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.FindFirst(Claims.Kind)?.Value != "platform")
            context.Result = new ForbidResult();
    }
}
