using System.ComponentModel.DataAnnotations;
using Dapper;
using E_Footwear_Api.Infrastructure;
using E_Footwear_Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace E_Footwear_Api.Modules.Auth;

public sealed record LoginRequest([Required] string CompanyCode, [Required] string UserName, [Required] string Password,
    long? StoreId, int? CounterNo);
public sealed record RefreshRequest([Required] string RefreshToken);
public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword);
public sealed record StoreDto(long Id, string Code, string Name);
public sealed record MeDto(long UserId, long CompanyId, string UserName, string? DisplayName, bool IsAdministrator,
    bool IsCompanyOwner, bool MustChangePassword, IReadOnlyList<StoreDto> Stores, IReadOnlyList<string> Permissions,
    bool CanEdit, bool CanDelete, bool CanPrint, bool CanChangePrice, bool CanGiveDiscount);
public sealed record AuthResult(string AccessToken, string RefreshToken, int ExpiresInSeconds, long StoreId, MeDto Me);

/// <summary>Lockout policy values are technical defaults (suggestion), configurable via "Security:*".</summary>
public sealed class SecurityOptions
{
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int MinPasswordLength { get; set; } = 8;
}

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IDb db, TokenService tokens, ICurrentUserAccessor accessor,
    IOptions<SecurityOptions> secOpt) : ControllerBase
{
    private readonly SecurityOptions _sec = secOpt.Value;
    // Used to spend the same hashing time when the user does not exist (avoids user enumeration by timing).
    private static readonly string DummyHash = PasswordHasher.Hash("dummy-password-for-timing");

    private sealed record LoginUser(long Id, long CompanyId, string UserName, string PasswordHash, string Status,
        int FailedLoginCount, DateTime? LockoutEnd);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<AuthResult> Login(LoginRequest req, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var user = await db.InSystemAsync(async (c, t) =>
        {
            var u = await c.QuerySingleOrDefaultAsync<LoginUser>(
                "select u.id, u.company_id as CompanyId, u.user_name as UserName, u.password_hash as PasswordHash, u.status, " +
                "u.failed_login_count as FailedLoginCount, u.lockout_end as LockoutEnd " +
                "from security.users u join platform.companies co on co.id=u.company_id " +
                "where lower(co.code)=lower(@code) and co.status in ('trial','active') and not co.is_deleted " +
                "and lower(u.user_name)=lower(@name) and not u.is_deleted",
                new { code = req.CompanyCode, name = req.UserName }, t);

            if (u is null) { PasswordHasher.Verify(req.Password, DummyHash); return null; }
            if (u.LockoutEnd is { } le && le > DateTime.UtcNow) throw ApiException.Unauthorized("Account temporarily locked. Try again later.");

            var ok = PasswordHasher.Verify(req.Password, u.PasswordHash);
            if (!ok || u.Status != "ACTIVE")
            {
                if (!ok)
                {
                    var failed = u.FailedLoginCount + 1;
                    DateTime? lockout = failed >= _sec.MaxFailedLogins ? DateTime.UtcNow.AddMinutes(_sec.LockoutMinutes) : null;
                    await c.ExecuteAsync("update security.users set failed_login_count=@f, lockout_end=@l where id=@id",
                        new { f = lockout is null ? failed : 0, l = lockout, id = u.Id }, t);
                    await Audit.Log(c, t, u.CompanyId, u.Id, u.UserName, "Login Failed", "user", u.Id, ip: ip);
                }
                return null;
            }
            await c.ExecuteAsync("update security.users set failed_login_count=0, lockout_end=null, last_login_at=now() where id=@id", new { id = u.Id }, t);
            return u;
        }, ct) ?? throw ApiException.Unauthorized();

        return await IssueSession(user.Id, user.CompanyId, req.StoreId, req.CounterNo, ip, "Log In", ct);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<AuthResult> Refresh(RefreshRequest req, CancellationToken ct)
    {
        var hash = TokenService.Hash(req.RefreshToken);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var row = await db.InSystemAsync(async (c, t) =>
        {
            var r = await c.QuerySingleOrDefaultAsync<RefreshRow>(
                "select id, company_id as CompanyId, user_id as UserId, store_id as StoreId, counter_no as CounterNo, " +
                "expires_at as ExpiresAt, revoked_at as RevokedAt from security.refresh_tokens where token_hash=@hash", new { hash }, t);
            if (r is null) return null;
            if (r.RevokedAt is not null)
            {   // a rotated token was presented again: assume theft and end every session of this user
                await c.ExecuteAsync("update security.refresh_tokens set revoked_at=now() where user_id=@u and revoked_at is null", new { u = r.UserId }, t);
                return null;
            }
            if (r.ExpiresAt < DateTime.UtcNow) return null;
            await c.ExecuteAsync("update security.refresh_tokens set revoked_at=now() where id=@id", new { id = r.Id }, t);
            return r;
        }, ct) ?? throw ApiException.Unauthorized("Invalid refresh token.");

        return await IssueSession(row.UserId, row.CompanyId, row.StoreId, row.CounterNo, ip, "Token Refresh", ct);
    }

    private sealed record RefreshRow(long Id, long CompanyId, long UserId, long StoreId, int? CounterNo, DateTime ExpiresAt, DateTime? RevokedAt);

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshRequest req, CancellationToken ct)
    {
        var user = await accessor.GetAsync(HttpContext, ct);
        var hash = TokenService.Hash(req.RefreshToken);
        await db.InSystemAsync(async (c, t) =>
        {
            await c.ExecuteAsync("update security.refresh_tokens set revoked_at=now() where token_hash=@hash and user_id=@u and revoked_at is null",
                new { hash, u = user.UserId }, t);
            await Audit.Log(c, t, user.CompanyId, user.UserId, user.UserName, "Log Out");
            return 0;
        }, ct);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await accessor.GetAsync(HttpContext, ct);
        if (req.NewPassword.Length < _sec.MinPasswordLength)
            throw ApiException.BadRequest("weak_password", $"Password must be at least {_sec.MinPasswordLength} characters.");
        await db.InSystemAsync(async (c, t) =>
        {
            var stored = await c.QuerySingleAsync<string>("select password_hash from security.users where id=@id", new { id = user.UserId }, t);
            if (!PasswordHasher.Verify(req.CurrentPassword, stored)) throw ApiException.Unauthorized("Current password is incorrect.");
            await c.ExecuteAsync("update security.users set password_hash=@h, must_change_password=false, updated_at=now(), updated_by=@id where id=@id",
                new { h = PasswordHasher.Hash(req.NewPassword), id = user.UserId }, t);
            await c.ExecuteAsync("update security.refresh_tokens set revoked_at=now() where user_id=@id and revoked_at is null", new { id = user.UserId }, t);
            await Audit.Log(c, t, user.CompanyId, user.UserId, user.UserName, "Change Password", "user", user.UserId);
            return 0;
        }, ct);
        return NoContent();
    }

    [HttpGet("/api/v1/me")]
    [Authorize]
    public async Task<MeDto> Me(CancellationToken ct) => await BuildMe(await accessor.GetAsync(HttpContext, ct), ct);

    private async Task<AuthResult> IssueSession(long userId, long companyId, long? requestedStore, int? counterNo, string? ip, string activity, CancellationToken ct)
    {
        var tokenCtx = new DefaultHttpContext();   // build identity without a JWT yet
        tokenCtx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(Claims.Kind, "tenant"),
            new System.Security.Claims.Claim(Claims.UserId, userId.ToString()),
            new System.Security.Claims.Claim(Claims.CompanyId, companyId.ToString()),
        }));
        var me = await accessor.GetAsync(tokenCtx, ct);
        if (me.StoreIds.Count == 0) throw ApiException.Forbidden("No store is assigned to this user.");
        var storeId = requestedStore ?? me.StoreIds[0];
        if (!me.StoreIds.Contains(storeId)) throw ApiException.Forbidden("You are not assigned to this store.");

        var (refresh, refreshHash) = TokenService.NewRefreshToken();
        await db.InSystemAsync(async (c, t) =>
        {
            await c.ExecuteAsync(
                "insert into security.refresh_tokens(company_id,user_id,token_hash,store_id,counter_no,expires_at,created_ip) " +
                "values (@companyId,@userId,@hash,@storeId,@counterNo,@exp,@ip::inet)",
                new { companyId, userId, hash = refreshHash, storeId, counterNo, exp = tokens.RefreshExpiry(), ip }, t);
            await Audit.Log(c, t, companyId, userId, me.UserName, activity, "user", userId, ip: ip);
            return 0;
        }, ct);

        return new AuthResult(tokens.CreateAccessToken(userId, companyId, "tenant"), refresh, tokens.AccessSeconds, storeId, await BuildMe(me, ct));
    }

    private async Task<MeDto> BuildMe(CurrentUser me, CancellationToken ct)
    {
        var (display, stores, allPerms) = await db.InTenantAsync(me.Scope, async (c, t) =>
        {
            var d = await c.QuerySingleAsync<string?>("select display_name from security.users where id=@id", new { id = me.UserId }, t);
            var s = (await c.QueryAsync<StoreDto>("select id, code, name from core.stores where not is_deleted and is_active order by name", transaction: t))
                .Where(x => me.StoreIds.Contains(x.Id)).ToList();
            var all = me.IsAdministrator || me.IsCompanyOwner
                ? (await c.QueryAsync<string>("select key from platform.permissions order by sort_order", transaction: t)).ToList()
                : me.Permissions.OrderBy(x => x).ToList();
            return (d, (IReadOnlyList<StoreDto>)s, (IReadOnlyList<string>)all);
        }, ct);
        return new MeDto(me.UserId, me.CompanyId, me.UserName, display, me.IsAdministrator, me.IsCompanyOwner, me.MustChangePassword,
            stores, allPerms, me.CanEdit, me.CanDelete, me.CanPrint, me.CanChangePrice, me.CanGiveDiscount);
    }
}
