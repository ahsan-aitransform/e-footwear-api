using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Dapper;
using E_Footwear_Api.Infrastructure;
using E_Footwear_Api.Modules.Auth;
using E_Footwear_Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace E_Footwear_Api.Modules.Platform;

public sealed record PlatformLoginRequest([Required] string UserName, [Required] string Password);
public sealed record PlatformAuthResult(string AccessToken, int ExpiresInSeconds, string UserName);

public sealed record RegisterCompanyRequest(
    [Required] string Code, [Required] string Name, string? Plan,
    [Required] string FirstStoreCode, [Required] string FirstStoreName,
    [Required] string OwnerUserName, string? OwnerDisplayName, [Required] string OwnerPassword);

public sealed record CompanyDto(long Id, string Code, string Name, string Status, DateTime CreatedAt);
public sealed record RegisteredCompany(long CompanyId, long FirstStoreId, long OwnerUserId);

[ApiController]
[Route("api/v1/platform")]
public sealed class PlatformController(IDb db, TokenService tokens, IOptions<SecurityOptions> secOpt) : ControllerBase
{
    private static readonly Regex CodeRx = new("^[a-z0-9][a-z0-9-]{1,29}$", RegexOptions.Compiled);
    private static readonly string DummyHash = PasswordHasher.Hash("dummy-password-for-timing");

    [HttpPost("auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<PlatformAuthResult> Login(PlatformLoginRequest req, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var row = await db.InSystemAsync(async (c, t) =>
        {
            var u = await c.QuerySingleOrDefaultAsync<PUser>(
                "select id, user_name as UserName, password_hash as PasswordHash, is_active as IsActive from platform.platform_users where lower(user_name)=lower(@n)", new { n = req.UserName }, t);
            if (u is null) { PasswordHasher.Verify(req.Password, DummyHash); return null; }
            if (!PasswordHasher.Verify(req.Password, u.PasswordHash) || !u.IsActive) return null;
            await c.ExecuteAsync("update platform.platform_users set last_login_at=now() where id=@id", new { id = u.Id }, t);
            await Audit.Log(c, t, null, u.Id, u.UserName, "Platform Log In", "platform_user", u.Id, ip: ip);
            return u;
        }, ct) ?? throw ApiException.Unauthorized();
        return new PlatformAuthResult(tokens.CreateAccessToken(row.Id, null, "platform"), tokens.AccessSeconds, row.UserName);
    }

    private sealed record PUser(long Id, string UserName, string PasswordHash, bool IsActive);

    /// <summary>Phase 3 "Company registration": provisions the tenant, its first store and its owner in one transaction.</summary>
    [HttpPost("companies")]
    [Authorize, PlatformOnly]
    public async Task<ActionResult<RegisteredCompany>> RegisterCompany(RegisterCompanyRequest req, CancellationToken ct)
    {
        var code = req.Code.Trim().ToLowerInvariant();
        if (!CodeRx.IsMatch(code)) throw ApiException.BadRequest("invalid_code", "Company code: 2-30 chars, a-z, 0-9 or '-'.");
        var min = secOpt.Value.MinPasswordLength;
        if (req.OwnerPassword.Length < min) throw ApiException.BadRequest("weak_password", $"Password must be at least {min} characters.");

        var adminId = long.Parse(User.FindFirst(Claims.UserId)!.Value);
        var result = await db.InSystemAsync(async (c, t) =>
        {
            if (await c.ExecuteScalarAsync<bool>("select exists(select 1 from platform.companies where lower(code)=@code)", new { code }, t))
                throw ApiException.Conflict("company_exists", "A company with this code already exists.");
            var plan = string.IsNullOrWhiteSpace(req.Plan) ? "BUSINESS" : req.Plan!.Trim().ToUpperInvariant();
            if (!await c.ExecuteScalarAsync<bool>("select exists(select 1 from platform.subscription_plans where code=@plan and is_active)", new { plan }, t))
                throw ApiException.BadRequest("unknown_plan", "Unknown subscription plan.");

            var companyId = await c.ExecuteScalarAsync<long>("select platform.provision_company(@code,@name,@plan)", new { code, name = req.Name.Trim(), plan }, t);
            var storeId = await c.ExecuteScalarAsync<long>("select core.create_store(@cid,@sc,@sn)",
                new { cid = companyId, sc = req.FirstStoreCode.Trim(), sn = req.FirstStoreName.Trim() }, t);
            var ownerId = await c.ExecuteScalarAsync<long>(
                "insert into security.users(company_id,user_name,display_name,password_hash,is_administrator,is_company_owner,can_edit,can_delete,can_print,can_change_price,can_give_discount,must_change_password) " +
                "values (@cid,@un,@dn,@ph,true,true,true,true,true,true,true,true) returning id",
                new { cid = companyId, un = req.OwnerUserName.Trim(), dn = req.OwnerDisplayName, ph = PasswordHasher.Hash(req.OwnerPassword) }, t);
            await c.ExecuteAsync(
                "insert into security.user_roles(company_id,user_id,role_id) select @cid,@uid,id from security.roles where company_id=@cid and name='Company Owner'",
                new { cid = companyId, uid = ownerId }, t);
            await c.ExecuteAsync("insert into security.user_stores(company_id,user_id,store_id,is_default) values (@cid,@uid,@sid,true)",
                new { cid = companyId, uid = ownerId, sid = storeId }, t);
            await Audit.Log(c, t, null, adminId, null, "Register Company", "company", companyId, remarks: code);
            return new RegisteredCompany(companyId, storeId, ownerId);
        }, ct);
        return Created($"/api/v1/platform/companies/{result.CompanyId}", result);
    }

    [HttpGet("companies")]
    [Authorize, PlatformOnly]
    public Task<IEnumerable<CompanyDto>> List(CancellationToken ct) =>
        db.InSystemAsync((c, t) => c.QueryAsync<CompanyDto>(
            "select id, code, name, status, created_at as CreatedAt from platform.companies where not is_deleted order by id", transaction: t), ct);
}
