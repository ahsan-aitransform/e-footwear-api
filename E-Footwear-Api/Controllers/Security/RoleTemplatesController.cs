using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Contracts;
using EFootwear.BLL.Security.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Security;

/// <summary>Role templates: named permission sets that prefill a user's permissions (Part 6). They never limit what a user can be given.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.RoleTemplates.Root)]
public sealed class RoleTemplatesController(RoleTemplateService roleTemplateService) : ControllerBase
{
    /// <summary>The system suggestions (one per role) followed by the company's own templates.</summary>
    [HttpGet]
    [RequirePermission(PermissionCodes.UserAccount.RolesAndPermissions.View)]
    public async Task<ActionResult<IReadOnlyList<RoleTemplateSummaryResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await roleTemplateService.ListAsync(cancellationToken));

    /// <summary>One template by key: a role code (a suggestion) or a template id.</summary>
    [HttpGet(ApiRoutes.RoleTemplates.ByKey)]
    [RequirePermission(PermissionCodes.UserAccount.RolesAndPermissions.View)]
    public async Task<ActionResult<RoleTemplateDetailsResponse>> Get(string templateKey, CancellationToken cancellationToken) =>
        await roleTemplateService.GetAsync(templateKey, cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCodes.UserAccount.RolesAndPermissions.Add)]
    public async Task<IActionResult> Create(SaveRoleTemplateRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, new CreatedIdResponse(await roleTemplateService.CreateAsync(request, cancellationToken)));

    [HttpPut(ApiRoutes.RoleTemplates.ById)]
    [RequirePermission(PermissionCodes.UserAccount.RolesAndPermissions.Edit)]
    public async Task<IActionResult> Update(Guid templateId, SaveRoleTemplateRequest request, CancellationToken cancellationToken)
    {
        await roleTemplateService.UpdateAsync(templateId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.RoleTemplates.ById)]
    [RequirePermission(PermissionCodes.UserAccount.RolesAndPermissions.Delete)]
    public async Task<IActionResult> Delete(Guid templateId, CancellationToken cancellationToken)
    {
        await roleTemplateService.DeleteAsync(templateId, cancellationToken);
        return NoContent();
    }
}
