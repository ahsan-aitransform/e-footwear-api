using System.ComponentModel.DataAnnotations;
using Dapper;
using E_Footwear_Api.Infrastructure;
using E_Footwear_Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Modules.Users;

public sealed class RoleDto
{
    public RoleDto() { }
    public RoleDto(long id, string name, bool isSystem, string[] keys) { Id = id; Name = name; IsSystem = isSystem; PermissionKeys = keys; }
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public bool IsSystem { get; init; }
    public string[] PermissionKeys { get; init; } = [];
}
public sealed record PermissionNode(string Key, string? ParentKey, string Module, string Label, int SortOrder, bool IsReport);
public sealed record CreateRoleRequest([Required] string Name);
public sealed record KeysRequest(string[] Keys);

/// <summary>Roles are templates only: per-user overrides (UsersController) always win (Part 6).</summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public sealed class RolesController(IDb db, ICurrentUserAccessor accessor) : ControllerBase
{
    /// <summary>Permission tree = the registered module/screen catalogue (platform.permissions); not hand-built in the UI.</summary>
    [HttpGet("permissions"), RequirePermission("UserAccount.ViewUser")]
    public async Task<IEnumerable<PermissionNode>> Tree(CancellationToken ct)
    {
        var u = await accessor.GetAsync(HttpContext, ct);
        return await db.InTenantAsync(u.Scope, (c, t) => c.QueryAsync<PermissionNode>(
            "select key, parent_key as ParentKey, module, label, sort_order as SortOrder, is_report as IsReport from platform.permissions order by sort_order", transaction: t), ct);
    }

    [HttpGet("roles"), RequirePermission("UserAccount.ViewUser")]
    public async Task<IEnumerable<RoleDto>> List(CancellationToken ct)
    {
        var u = await accessor.GetAsync(HttpContext, ct);
        return await db.InTenantAsync(u.Scope, (c, t) => c.QueryAsync<RoleDto>(
            "select r.id, r.name, r.is_system as IsSystem, " +
            "coalesce((select array_agg(permission_key order by permission_key) from security.role_permissions p where p.role_id=r.id), '{}') as PermissionKeys " +
            "from security.roles r where not r.is_deleted order by r.name", transaction: t), ct);
    }

    [HttpPost("roles"), RequirePermission("UserAccount.CreateNewUser")]
    public async Task<ActionResult<RoleDto>> CreateRole(CreateRoleRequest req, CancellationToken ct)
    {
        var u = await accessor.GetAsync(HttpContext, ct);
        var dto = await db.InTenantAsync(u.Scope, async (c, t) =>
        {
            var id = await c.ExecuteScalarAsync<long>("insert into security.roles(company_id,name,created_by) values (@cid,@n,@by) returning id",
                new { cid = u.CompanyId, n = req.Name.Trim(), by = u.UserId }, t);
            await Audit.Log(c, t, u.CompanyId, u.UserId, u.UserName, "New Role", "role", id, remarks: req.Name);
            return new RoleDto(id, req.Name.Trim(), false, []);
        }, ct);
        return Created($"/api/v1/roles/{dto.Id}", dto);
    }

    [HttpPut("roles/{id:long}/permissions"), RequirePermission("UserAccount.CreateNewUser")]
    public async Task<IActionResult> SetPermissions(long id, KeysRequest req, CancellationToken ct)
    {
        var u = await accessor.GetAsync(HttpContext, ct);
        // No privilege escalation: a manager can only put permissions they hold themselves into a role.
        if (req.Keys.Any(k => !u.Has(k))) throw ApiException.Forbidden("Cannot grant a permission you do not hold.");
        await db.InTenantAsync(u.Scope, async (c, t) =>
        {
            if (!await c.ExecuteScalarAsync<bool>("select exists(select 1 from security.roles where id=@id and not is_deleted)", new { id }, t))
                throw ApiException.NotFound("Role not found.");
            await c.ExecuteAsync("delete from security.role_permissions where role_id=@id", new { id }, t);
            foreach (var k in req.Keys.Distinct())
                await c.ExecuteAsync("insert into security.role_permissions(company_id,role_id,permission_key) values (@cid,@id,@k)", new { cid = u.CompanyId, id, k }, t);
            await Audit.Log(c, t, u.CompanyId, u.UserId, u.UserName, "Set Role Permissions", "role", id);
            return 0;
        }, ct);
        return NoContent();
    }
}
