using EFootwear.BLL.Auth;
using EFootwear.BLL.Common;
using EFootwear.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace E_Footwear_Api.Controllers;

public sealed record ChangePasswordBody(string CurrentPassword, string NewPassword);

// API / Presentation layer: controllers only translate HTTP <-> BLL services. No business rules here.
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService auth, RequestContext me) : ControllerBase
{
    /// <summary>Company sign-in. Platform administrators use /api/v1/platform/auth/login.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResult>> Login(LoginRequest request, CancellationToken ct) =>
        await auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(),
                              Request.Headers.UserAgent.ToString(), ct);

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var raw = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        if (!AuthService.TryParseToken(raw, out var companyId, out var secret))
            throw new DomainException(ErrorKind.Unauthorized, "Malformed token.");
        await auth.LogoutAsync(companyId, secret, ct);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordBody body, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(me, body.CurrentPassword, body.NewPassword, ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<object> Me() => me.IsPlatformAdmin
        ? Ok(new { platformAdmin = true, adminId = me.PlatformAdminId, username = me.PlatformAdminName })
        : Ok(new
        {
            platformAdmin = false, me.CompanyId, me.UserId, me.RoleCode, me.AllStores, me.StoreIds, me.MustChangePassword,
        });
}
