using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Security.Contracts;
using EFootwear.BLL.Security.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Security;

/// <summary>Sessions of other users and the sign-in history (Part 6). A person's own sessions are in <see cref="AccountController"/>.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Users.Root)]
public sealed class UserSessionsController(UserSessionService userSessionService) : ControllerBase
{
    [HttpGet(ApiRoutes.Users.Sessions)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.View)]
    public async Task<ActionResult<IReadOnlyList<UserSessionResponse>>> ListSessions(Guid userId, CancellationToken cancellationToken) =>
        Ok(await userSessionService.ListSessionsOfUserAsync(userId, cancellationToken));

    [HttpDelete(ApiRoutes.Users.SessionById)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.Edit)]
    public async Task<IActionResult> EndSession(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        await userSessionService.EndSessionOfUserAsync(userId, sessionId, cancellationToken);
        return NoContent();
    }

    /// <summary>Sign-in history of one user (?userId=) or of everyone the caller may see. Dates default to the last 30 days.</summary>
    [HttpGet(ApiRoutes.Users.LoginHistory)]
    [RequirePermission(PermissionCodes.UserAccount.LoginHistory.View)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Administration.LoginHistoryView)]
    public async Task<ActionResult<IReadOnlyList<LoginHistoryEntryResponse>>> ListLoginHistory(
        [FromQuery] Guid? userId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken) =>
        Ok(await userSessionService.ListLoginHistoryAsync(userId, from, to, cancellationToken));
}
