using E_Footwear_Api.Authentication;
using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Security.Contracts;
using EFootwear.BLL.Security.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Security;

/// <summary>
/// The signed-in person's own account: sign-in history and the quick-action toolbar. These endpoints need no permission code;
/// they only ever touch the caller's own rows.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Account.Root)]
public sealed class AccountController(
    UserSessionService userSessionService,
    UserQuickActionService userQuickActionService) : ControllerBase
{
    [HttpGet(ApiRoutes.Account.LoginHistory)]
    public async Task<ActionResult<IReadOnlyList<LoginHistoryEntryResponse>>> ListOwnLoginHistory(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken) =>
        Ok(await userSessionService.ListOwnLoginHistoryAsync(from, to, cancellationToken));

    [HttpGet(ApiRoutes.Account.QuickActions)]
    public async Task<ActionResult<UserQuickActionsResponse>> GetOwnQuickActions(CancellationToken cancellationToken) =>
        await userQuickActionService.GetOwnAsync(cancellationToken);
}
