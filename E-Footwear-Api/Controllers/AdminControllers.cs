using EFootwear.Application.Abstractions;
using EFootwear.Application.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EFootwear.Api.Controllers;

[ApiController, Authorize, Route("api/v1/users")]
public sealed class UsersController(UserService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery] PageQuery q, string? status, CancellationToken ct) => Ok(await svc.ListAsync(me.User, q, status, ct));
    [HttpGet("meta")] public async Task<IActionResult> Meta(CancellationToken ct) => Ok(await svc.MetaAsync(me.User, ct));
    [HttpGet("{id:long}")] public async Task<IActionResult> Get(long id, CancellationToken ct) => Ok(await svc.GetAsync(me.User, id, ct));

    [HttpPost]
    public async Task<IActionResult> Create(UserInput body, CancellationToken ct)
    {
        var id = await svc.CreateAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:long}")] public async Task<IActionResult> Update(long id, UserUpdate body, CancellationToken ct) { await svc.UpdateAsync(me.User, id, body, ct); return NoContent(); }
    [HttpPost("{id:long}/reset-password")] public async Task<IActionResult> Reset(long id, ResetPasswordRequest body, CancellationToken ct) { await svc.ResetPasswordAsync(me.User, id, body, ct); return NoContent(); }
    [HttpPost("{id:long}/status")] public async Task<IActionResult> Status(long id, StatusRequest body, CancellationToken ct) { await svc.SetStatusAsync(me.User, id, body, ct); return NoContent(); }

    [HttpGet("login-history")]
    public async Task<IActionResult> History(long? userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) => Ok(await svc.LoginHistoryAsync(me.User, userId, from, to, ct));
}

[ApiController, Authorize, Route("api/v1")]
public sealed class StoresController(StoreService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet("stores")] public async Task<IActionResult> List(bool includeInactive, CancellationToken ct) => Ok(await svc.ListAsync(me.User, includeInactive, ct));
    [HttpGet("stores/{id:long}")] public async Task<IActionResult> Get(long id, CancellationToken ct) => Ok(await svc.GetAsync(me.User, id, ct));

    [HttpPost("stores")]
    public async Task<IActionResult> Create(StoreInput body, CancellationToken ct)
    {
        var id = await svc.CreateAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("stores/{id:long}")] public async Task<IActionResult> Update(long id, StoreInput body, CancellationToken ct) { await svc.UpdateAsync(me.User, id, body, ct); return NoContent(); }
    [HttpGet("devices")] public async Task<IActionResult> Devices(CancellationToken ct) => Ok(await svc.DevicesAsync(me.User, ct));
    [HttpGet("company")] public async Task<IActionResult> Company(CancellationToken ct) => Ok(await svc.GetCompanyAsync(me.User, ct));
    [HttpPut("company")] public async Task<IActionResult> UpdateCompany(CompanyInput body, CancellationToken ct) { await svc.UpdateCompanyAsync(me.User, body, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/v1/masters")]
public sealed class MastersController(MasterService svc, ICurrentUser me) : ControllerBase
{
    // Fixed routes first: "categories" and "sizes" are not generic lists.
    [HttpGet("categories")] public async Task<IActionResult> Categories(string? search, long? departmentId, bool includeInactive, CancellationToken ct) => Ok(await svc.CategoriesAsync(me.User, search, departmentId, includeInactive, ct));

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory(CategoryBody body, CancellationToken ct) => Created("", new { id = await svc.CreateCategoryAsync(me.User, body, ct) });

    [HttpPut("categories/{id:long}")] public async Task<IActionResult> UpdateCategory(long id, CategoryBody body, CancellationToken ct) { await svc.UpdateCategoryAsync(me.User, id, body, ct); return NoContent(); }
    [HttpDelete("categories/{id:long}")] public async Task<IActionResult> DeleteCategory(long id, CancellationToken ct) { await svc.DeleteCategoryAsync(me.User, id, ct); return NoContent(); }

    [HttpGet("sizes")] public async Task<IActionResult> Sizes(long? sizeGroupId, CancellationToken ct) => Ok(await svc.SizesAsync(me.User, sizeGroupId, ct));
    [HttpPost("sizes")] public async Task<IActionResult> CreateSize(SizeBody body, CancellationToken ct) => Created("", new { id = await svc.CreateSizeAsync(me.User, body, ct) });
    [HttpDelete("sizes/{id:long}")] public async Task<IActionResult> DeleteSize(long id, CancellationToken ct) { await svc.DeleteSizeAsync(me.User, id, ct); return NoContent(); }

    [HttpGet("{kind}")] public async Task<IActionResult> List(string kind, string? search, bool includeInactive, CancellationToken ct) => Ok(await svc.ListAsync(me.User, kind, search, includeInactive, ct));
    [HttpPost("{kind}")] public async Task<IActionResult> Create(string kind, MasterBody body, CancellationToken ct) => Created("", new { id = await svc.CreateAsync(me.User, kind, body, ct) });
    [HttpPut("{kind}/{id:long}")] public async Task<IActionResult> Update(string kind, long id, MasterBody body, CancellationToken ct) { await svc.UpdateAsync(me.User, kind, id, body, ct); return NoContent(); }
    [HttpDelete("{kind}/{id:long}")] public async Task<IActionResult> Delete(string kind, long id, CancellationToken ct) { await svc.DeleteAsync(me.User, kind, id, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/v1/parties")]
public sealed class PartiesController(PartyService svc, ICurrentUser me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageQuery q, bool? customer, bool? vendor, bool? staff, string? status, bool onlyWithBalance, CancellationToken ct)
        => Ok(await svc.ListAsync(me.User, new PartyFilter(customer, vendor, staff, status, onlyWithBalance), q, ct));

    [HttpGet("{id:long}")] public async Task<IActionResult> Get(long id, CancellationToken ct) => Ok(await svc.GetAsync(me.User, id, ct));

    [HttpPost]
    public async Task<IActionResult> Create(PartyBody body, CancellationToken ct)
    {
        var id = await svc.CreateAsync(me.User, body, ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:long}")] public async Task<IActionResult> Update(long id, PartyBody body, CancellationToken ct) { await svc.UpdateAsync(me.User, id, body, ct); return NoContent(); }
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id, CancellationToken ct) { await svc.DeleteAsync(me.User, id, ct); return NoContent(); }
    [HttpPost("{id:long}/move-staff")] public async Task<IActionResult> Move(long id, MoveStaffBody body, CancellationToken ct) { await svc.MoveStaffAsync(me.User, id, body, ct); return NoContent(); }
    [HttpGet("{id:long}/staff-history")] public async Task<IActionResult> History(long id, CancellationToken ct) => Ok(await svc.StaffHistoryAsync(me.User, id, ct));
}
