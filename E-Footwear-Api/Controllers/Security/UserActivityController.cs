using E_Footwear_Api.Authorization;
using E_Footwear_Api.Binding;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Contracts;
using EFootwear.BLL.Security.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Security;

/// <summary>Users Activity (docx 1.4): the audit feed of data changes and sign-in events.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Users.Root)]
public sealed class UserActivityController(UserActivityService userActivityService) : ControllerBase
{
    /// <summary>Filters: from, to, userId, entryKind (BUSINESS_CHANGE or SIGN_IN); paging and free text through ?page=&amp;pageSize=&amp;sort=&amp;search=.</summary>
    [HttpGet(ApiRoutes.Users.Activity)]
    [RequirePermission(PermissionCodes.UserAccount.UsersActivity.View)]
    public async Task<ActionResult<PagedResult<UserActivityEntryResponse>>> Query(
        [FromQuery] PageQuery pageQuery, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? userId, [FromQuery] string? entryKind, CancellationToken cancellationToken) =>
        await userActivityService.QueryAsync(new UserActivityQuery(from, to, userId, entryKind, pageQuery.ToPageRequest()), cancellationToken);
}
