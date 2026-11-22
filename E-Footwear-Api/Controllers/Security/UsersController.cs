using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Contracts;
using EFootwear.BLL.Security.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Security;

/// <summary>
/// User management (docx 1.1 Create New User, 1.3 Users Account). Who may create which role, and in which stores, is decided by the BLL
/// (UserCreationRules); who may change another user by UserManagementRules. A platform administrator passes companyId to list and create;
/// a company user never does. Listing and creating allow platform administrators explicitly; everything else is for company users.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiRoutes.Users.Root)]
public sealed class UsersController(
    UserManagementService userManagementService,
    UserAccountSecurityService userAccountSecurityService,
    UserQuickActionService userQuickActionService) : ControllerBase
{
    /// <summary>Roles and stores the caller may use when creating a user.</summary>
    [HttpGet(ApiRoutes.Users.CreationOptions)]
    [RequirePermission(PermissionCodes.UserAccount.CreateUser.Add, AllowPlatformAdministrator = true)]
    public async Task<ActionResult<UserCreationOptionsResponse>> GetCreationOptions(
        [FromQuery] Guid? companyId, CancellationToken cancellationToken) =>
        await userManagementService.GetCreationOptionsAsync(companyId, cancellationToken);

    [HttpGet]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.View, AllowPlatformAdministrator = true)]
    public async Task<ActionResult<IReadOnlyList<UserListItemResponse>>> List(
        [FromQuery] Guid? companyId, CancellationToken cancellationToken) =>
        Ok(await userManagementService.ListAsync(companyId, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.UserAccount.CreateUser.Add, AllowPlatformAdministrator = true)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created,
                   new CreatedIdResponse(await userManagementService.CreateAsync(request, cancellationToken)));

    [HttpGet(ApiRoutes.Users.ById)]
    [Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.View)]
    public async Task<ActionResult<UserDetailsResponse>> GetDetails(Guid userId, CancellationToken cancellationToken) =>
        await userManagementService.GetDetailsAsync(userId, cancellationToken);

    [HttpPut(ApiRoutes.Users.ById)]
    [Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.Edit)]
    public async Task<IActionResult> Update(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        await userManagementService.UpdateAsync(userId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.Users.ById)]
    [Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.Delete)]
    public async Task<IActionResult> Delete(Guid userId, CancellationToken cancellationToken)
    {
        await userManagementService.DeleteAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpPost(ApiRoutes.Users.Block)]
    [Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Administration.UserBlock)]
    public async Task<IActionResult> Block(Guid userId, CancellationToken cancellationToken)
    {
        await userAccountSecurityService.BlockAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpPost(ApiRoutes.Users.Unblock)]
    [Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Administration.UserBlock)]
    public async Task<IActionResult> Unblock(Guid userId, CancellationToken cancellationToken)
    {
        await userAccountSecurityService.UnblockAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>An administrator sets a temporary password (docx 1.3 "Reset Password"). All sessions of the user end.</summary>
    [HttpPost(ApiRoutes.Users.ResetPassword)]
    [Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Administration.UserPasswordReset)]
    public async Task<IActionResult> ResetPassword(Guid userId, ResetUserPasswordRequest request, CancellationToken cancellationToken)
    {
        await userAccountSecurityService.ResetPasswordAsync(userId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Issues a single-use reset token, shown once, for the administrator to hand to the user.</summary>
    [HttpPost(ApiRoutes.Users.PasswordResetToken)]
    [Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.Edit)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Administration.UserPasswordReset)]
    public async Task<ActionResult<IssuedPasswordResetTokenResponse>> IssuePasswordResetToken(Guid userId, CancellationToken cancellationToken) =>
        await userAccountSecurityService.IssuePasswordResetTokenAsync(
            userId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

    /// <summary>The quick-action toolbar (docx Standard Menu) of a user.</summary>
    [HttpGet(ApiRoutes.Users.QuickActions)]
    [Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.View)]
    public async Task<ActionResult<UserQuickActionsResponse>> GetQuickActions(Guid userId, CancellationToken cancellationToken) =>
        await userQuickActionService.GetForUserAsync(userId, cancellationToken);

    [HttpPut(ApiRoutes.Users.QuickActions)]
    [Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
    [RequirePermission(PermissionCodes.UserAccount.ViewUsers.Edit)]
    public async Task<IActionResult> ReplaceQuickActions(Guid userId, ReplaceUserQuickActionsRequest request, CancellationToken cancellationToken)
    {
        await userQuickActionService.ReplaceForUserAsync(userId, request, cancellationToken);
        return NoContent();
    }
}
