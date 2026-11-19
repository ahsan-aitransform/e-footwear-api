using System.ComponentModel.DataAnnotations;
using Dapper;
using E_Footwear_Api.Infrastructure;
using E_Footwear_Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Modules.Core;

public sealed record StoreDetail(long Id, string Code, string Name, string? NameUr, string? Address, string? Phone,
    string? Mobile, string? Email, string? CityName, bool IsActive);
public sealed record StoreInput([Required] string Code, [Required] string Name, string? NameUr, string? Address,
    string? Phone, string? Mobile, string? Email, string? CityName);
public sealed record StoreUpdate([Required] string Name, string? NameUr, string? Address, string? Phone,
    string? Mobile, string? Email, string? CityName);

/// <summary>Phase 3 "Store management" (Part 5, 7). Company id comes from the token, never from the request.</summary>
[ApiController]
[Route("api/v1/stores")]
[Authorize]
public sealed class StoresController(IDb db, ICurrentUserAccessor accessor) : ControllerBase
{
    private const string Cols = "id, code, name, name_ur as NameUr, address, phone, mobile, email, city_name as CityName, is_active as IsActive";

    [HttpGet, RequirePermission("Settings.Stores")]
    public async Task<IEnumerable<StoreDetail>> List(CancellationToken ct)
    {
        var u = await accessor.GetAsync(HttpContext, ct);
        return await db.InTenantAsync(u.Scope, (c, t) => c.QueryAsync<StoreDetail>(
            $"select {Cols} from core.stores where not is_deleted and (@all or id = any(@ids)) order by name",
            new { all = u.AllStores, ids = u.StoreIds.ToArray() }, t), ct);
    }

    [HttpPost, RequirePermission("Settings.Stores")]
    public async Task<ActionResult<StoreDetail>> Create(StoreInput req, CancellationToken ct)
    {
        var u = await accessor.GetAsync(HttpContext, ct);
        if (!u.IsAdministrator && !u.IsCompanyOwner && !u.CanEdit) throw ApiException.Forbidden();
        var s = await db.InTenantAsync(u.Scope, async (c, t) =>
        {
            var id = await c.ExecuteScalarAsync<long>("select core.create_store(@cid,@code,@name)",
                new { cid = u.CompanyId, code = req.Code.Trim(), name = req.Name.Trim() }, t);
            await c.ExecuteAsync(
                "update core.stores set name_ur=@NameUr, address=@Address, phone=@Phone, mobile=@Mobile, email=@Email, city_name=@CityName, created_by=@uid where id=@id",
                new { req.NameUr, req.Address, req.Phone, req.Mobile, req.Email, req.CityName, uid = u.UserId, id }, t);
            await Audit.Log(c, t, u.CompanyId, u.UserId, u.UserName, "New Store", "store", id, remarks: req.Code);
            return await c.QuerySingleAsync<StoreDetail>($"select {Cols} from core.stores where id=@id", new { id }, t);
        }, ct);
        return Created($"/api/v1/stores/{s.Id}", s);
    }

    [HttpPut("{id:long}"), RequirePermission("Settings.Stores")]
    public async Task<StoreDetail> Update(long id, StoreUpdate req, CancellationToken ct)
    {
        var u = await accessor.GetAsync(HttpContext, ct);
        if (!u.IsAdministrator && !u.IsCompanyOwner && !u.CanEdit) throw ApiException.Forbidden();
        return await db.InTenantAsync(u.Scope, async (c, t) =>
        {
            var n = await c.ExecuteAsync(
                "update core.stores set name=@Name, name_ur=@NameUr, address=@Address, phone=@Phone, mobile=@Mobile, email=@Email, " +
                "city_name=@CityName, updated_at=now(), updated_by=@uid where id=@id and not is_deleted and (@all or id = any(@ids))",
                new { req.Name, req.NameUr, req.Address, req.Phone, req.Mobile, req.Email, req.CityName, uid = u.UserId, id, all = u.AllStores, ids = u.StoreIds.ToArray() }, t);
            if (n == 0) throw ApiException.NotFound();
            await Audit.Log(c, t, u.CompanyId, u.UserId, u.UserName, "Edit Store", "store", id);
            return await c.QuerySingleAsync<StoreDetail>($"select {Cols} from core.stores where id=@id", new { id }, t);
        }, ct);
    }

    [HttpPost("{id:long}/{verb:regex(^(activate|deactivate)$)}"), RequirePermission("Settings.Stores")]
    public async Task<IActionResult> SetActive(long id, string verb, CancellationToken ct)
    {
        var u = await accessor.GetAsync(HttpContext, ct);
        if (!u.IsAdministrator && !u.IsCompanyOwner) throw ApiException.Forbidden();
        await db.InTenantAsync(u.Scope, async (c, t) =>
        {
            var n = await c.ExecuteAsync("update core.stores set is_active=@a, updated_at=now(), updated_by=@uid where id=@id and not is_deleted",
                new { a = verb == "activate", uid = u.UserId, id }, t);
            if (n == 0) throw ApiException.NotFound();
            await Audit.Log(c, t, u.CompanyId, u.UserId, u.UserName, verb == "activate" ? "Activate Store" : "Deactivate Store", "store", id);
            return 0;
        }, ct);
        return NoContent();
    }
}
