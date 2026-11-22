using E_Footwear_Api.Authorization;
using E_Footwear_Api.Constants;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.BLL.Platform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace E_Footwear_Api.Controllers.Platform;

/// <summary>Platform administrator sign-in. Separate from company sign-in.</summary>
[ApiController]
[Route(ApiRoutes.PlatformAuthentication.Root)]
public sealed class PlatformAdministratorAuthenticationController(
    PlatformAdministratorAuthenticationService authenticationService) : ControllerBase
{
    [HttpPost(ApiRoutes.PlatformAuthentication.Login)]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimit.PolicyName)]
    public async Task<ActionResult<PlatformLoginResponse>> Login(PlatformLoginRequest request, CancellationToken cancellationToken) =>
        await authenticationService.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(),
                                               Request.Headers.UserAgent.ToString(), cancellationToken);

    [HttpPost(ApiRoutes.PlatformAuthentication.Logout)]
    [Authorize(Policy = AuthorizationPolicies.PlatformAdministratorOnly)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var rawToken = Request.Headers.Authorization.ToString()[HttpAuthorizationScheme.BearerPrefix.Length..].Trim();
        if (PlatformAdministratorAuthenticationService.TryGetPlatformSecret(rawToken, out var secret))
            await authenticationService.LogoutAsync(secret, cancellationToken);
        return NoContent();
    }
}
