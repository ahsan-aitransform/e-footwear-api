using EFootwear.BLL.Platform;
using EFootwear.BLL.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace E_Footwear_Api.Controllers;

/// <summary>Platform administrator sign-in. Separate from company sign-in.</summary>
[ApiController]
[Route("api/v1/platform/auth")]
public sealed class PlatformAuthController(PlatformAuthService auth) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<PlatformLoginResult>> Login(PlatformLoginRequest request, CancellationToken ct) =>
        await auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(),
                              Request.Headers.UserAgent.ToString(), ct);

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var raw = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        if (PlatformAuthService.TryGetPlatformSecret(raw, out var secret))
            await auth.LogoutAsync(secret, ct);
        return NoContent();
    }
}

/// <summary>Platform administration: companies and their stores. Platform administrators only.</summary>
[ApiController]
[Authorize]
[Route("api/v1/platform")]
public sealed class PlatformController(PlatformService platform) : ControllerBase
{
    [HttpGet("companies")]
    public async Task<IActionResult> ListCompanies(CancellationToken ct) =>
        Ok(await platform.ListCompaniesAsync(ct));

    [HttpPost("companies")]
    public async Task<IActionResult> CreateCompany(CreateCompanyRequest body, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await platform.CreateCompanyAsync(body, ct));

    [HttpGet("companies/{companyId:guid}/stores")]
    public async Task<IActionResult> ListStores(Guid companyId, CancellationToken ct) =>
        Ok(await platform.ListStoresAsync(companyId, ct));

    [HttpPost("companies/{companyId:guid}/stores")]
    public async Task<IActionResult> CreateStore(Guid companyId, CreateStoreRequest body, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { id = await platform.CreateStoreAsync(companyId, body, ct) });
}
