using E_Footwear_Api.Authorization;
using E_Footwear_Api.Constants;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Contracts;
using EFootwear.BLL.Security.Services;
using EFootwear.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace E_Footwear_Api.Controllers.Security;

// API / Presentation layer: controllers only translate HTTP <-> BLL services. No business rules here.
[ApiController]
[Route(ApiRoutes.Authentication.Root)]
public sealed class AuthenticationController(
    UserAuthenticationService authenticationService,
    CurrentSessionService currentSessionService,
    PasswordResetService passwordResetService,
    RequestContext requestContext) : ControllerBase
{
    /// <summary>
    /// Forgot password. Always answers 202, whether or not the email has an account. How the token reaches the person is
    /// [BUSINESS DECISION REQUIRED]: until a channel is approved nothing is sent and an administrator issues the token.
    /// </summary>
    [HttpPost(ApiRoutes.Authentication.RequestPasswordReset)]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimit.PolicyName)]
    public async Task<IActionResult> RequestPasswordReset(RequestPasswordResetRequest request, CancellationToken cancellationToken)
    {
        await passwordResetService.RequestAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return Accepted();
    }

    /// <summary>Completes a reset with the single-use token and a new password. Ends every session of the user.</summary>
    [HttpPost(ApiRoutes.Authentication.CompletePasswordReset)]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimit.PolicyName)]
    public async Task<IActionResult> CompletePasswordReset(CompletePasswordResetRequest request, CancellationToken cancellationToken)
    {
        await passwordResetService.CompleteAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Company sign-in. Platform administrators use /api/v1/platform/auth/login.</summary>
    [HttpPost(ApiRoutes.Authentication.Login)]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimit.PolicyName)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken) =>
        await authenticationService.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(),
                                               Request.Headers.UserAgent.ToString(), cancellationToken);

    [HttpPost(ApiRoutes.Authentication.Logout)]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var rawToken = Request.Headers.Authorization.ToString()[HttpAuthorizationScheme.BearerPrefix.Length..].Trim();
        if (!UserAuthenticationService.TryParseToken(rawToken, out var companyId, out var secret))
            throw new DomainException(ErrorKind.Unauthorized, "Malformed token.");
        await authenticationService.LogoutAsync(companyId, secret, cancellationToken);
        return NoContent();
    }

    /// <summary>Changes the caller's own password. Needs no permission: a user with a temporary password must be able to.</summary>
    [HttpPost(ApiRoutes.Authentication.ChangePassword)]
    [Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await authenticationService.ChangePasswordAsync(requestContext, request.CurrentPassword, request.NewPassword, cancellationToken);
        return NoContent();
    }

    /// <summary>Who is signed in and what they may do. Works for company users and platform administrators.</summary>
    [HttpGet(ApiRoutes.Authentication.CurrentSession)]
    [Authorize]
    public async Task<ActionResult<CurrentSessionResponse>> GetCurrentSession(CancellationToken cancellationToken) =>
        await currentSessionService.GetCurrentSessionAsync(cancellationToken);
}
