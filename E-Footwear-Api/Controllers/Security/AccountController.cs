using E_Footwear_Api.Authentication;
using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Security.Contracts;
using EFootwear.BLL.Security.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Security;

/// <summary>
/// The signed-in person's own account: sessions, sign-in history and the quick-action toolbar. These endpoints need no permission code
/// (every company user may see and end their own sessions, for example after losing a device); they only ever touch the caller's own rows.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Account.Root)]
public sealed class AccountController(
    UserSessionService userSessionService,
    UserQuickActionService userQuickActionService) : ControllerBase
{
    [HttpGet(ApiRoutes.Account.Sessions)]
    public async Task<ActionResult<IReadOnlyList<UserSessionResponse>>> ListOwnSessions(CancellationToken cancellationToken) =>
        Ok(await userSessionService.ListOwnSessionsAsync(CurrentSessionSecretReader.Read(Request), cancellationToken));

    [HttpDelete(ApiRoutes.Account.SessionById)]
    public async Task<IActionResult> EndOwnSession(Guid sessionId, CancellationToken cancellationToken)
    {
        await userSessionService.EndOwnSessionAsync(sessionId, cancellationToken);
        return NoContent();
    }

    /// <summary>Ends every session of the caller except the one in use.</summary>
    [HttpDelete(ApiRoutes.Account.OtherSessions)]
    public async Task<IActionResult> EndOtherOwnSessions(CancellationToken cancellationToken)
    {
        var endedCount = await userSessionService.EndOtherOwnSessionsAsync(CurrentSessionSecretReader.Read(Request), cancellationToken);
        return Ok(new { endedCount });
    }

    [HttpGet(ApiRoutes.Account.LoginHistory)]
    public async Task<ActionResult<IReadOnlyList<LoginHistoryEntryResponse>>> ListOwnLoginHistory(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken) =>
        Ok(await userSessionService.ListOwnLoginHistoryAsync(from, to, cancellationToken));

    [HttpGet(ApiRoutes.Account.QuickActions)]
    public async Task<ActionResult<UserQuickActionsResponse>> GetOwnQuickActions(CancellationToken cancellationToken) =>
        await userQuickActionService.GetOwnAsync(cancellationToken);
}
