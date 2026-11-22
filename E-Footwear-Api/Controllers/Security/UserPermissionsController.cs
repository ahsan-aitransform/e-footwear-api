using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Security.Contracts;
using EFootwear.BLL.Security.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Security;

/// <summary>The permissions of one user (Part 6, docx 1.2). Changing them needs the Roles and Permissions screen permission.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Users.Root)]
public sealed class UserPermissionsController(UserPermissionService userPermissionService) : ControllerBase
{
    [HttpGet(ApiRoutes.Users.Permissions)]
    [RequirePermission(PermissionCodes.UserAccount.RolesAndPermissions.View)]
    public async Task<ActionResult<UserPermissionsResponse>> Get(Guid userId, CancellationToken cancellationToken) =>
        await userPermissionService.GetAsync(userId, cancellationToken);

    [HttpPut(ApiRoutes.Users.Permissions)]
    [RequirePermission(PermissionCodes.UserAccount.RolesAndPermissions.Edit)]
    public async Task<IActionResult> Replace(Guid userId, ReplaceUserPermissionsRequest request, CancellationToken cancellationToken)
    {
        await userPermissionService.ReplaceAsync(userId, request, cancellationToken);
        return NoContent();
    }
}
