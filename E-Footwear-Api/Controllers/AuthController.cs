using EFootwear.Api.Security;
using EFootwear.Application.Abstractions;
using EFootwear.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EFootwear.Api.Controllers;

[ApiController, Route("api/v1/auth")]
public sealed class AuthController(AuthService auth, ICurrentUser current) : ControllerBase
{
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Company is its numeric id or its exact name (the schema has no short company code).</summary>
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        var device = Request.Headers[SessionDefaults.DeviceHeader].FirstOrDefault();
        var r = await auth.LoginAsync(req, Ip, device, Request.Headers.UserAgent.ToString(), ct);
        return Ok(new { token = r.Token, mustChangePassword = r.MustChangePassword, me = r.Me });
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim(), ct);
        return NoContent();
    }

    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me(CancellationToken ct) => Ok(await auth.MeAsync(current.User, ct));

    [HttpPost("change-password"), Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(current.User, req, ct);
        return NoContent();   // every session of the user is revoked by the database; sign in again
    }
}
