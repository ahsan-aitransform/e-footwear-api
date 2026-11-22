using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.BLL.MasterData.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.MasterData;

/// <summary>
/// Categories (docx 2.5): each belongs to a department and may sit under another category of the same department.
/// The "options" endpoint feeds the category dropdown of other screens and needs no Settings permission.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.MasterData.Categories)]
public sealed class CategoriesController(CategoryService categoryService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Settings.Category.View)]
    public async Task<ActionResult<PagedResult<CategoryResponse>>> List(
        [FromQuery] Guid? departmentId, [FromQuery] PageRequest pageRequest, [FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        Ok(await categoryService.ListAsync(departmentId, pageRequest, includeInactive, cancellationToken));

    [HttpGet(ApiRoutes.MasterData.Options)]
    public async Task<ActionResult<IReadOnlyList<MasterListOptionResponse>>> ListOptions([FromQuery] Guid? departmentId, CancellationToken cancellationToken) =>
        Ok(await categoryService.ListOptionsAsync(departmentId, cancellationToken));

    [HttpGet(ApiRoutes.MasterData.ItemById)]
    [RequirePermission(PermissionCodes.Settings.Category.View)]
    public async Task<ActionResult<CategoryResponse>> Get(Guid itemId, CancellationToken cancellationToken) =>
        Ok(await categoryService.GetAsync(itemId, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.Settings.Category.Add)]
    public async Task<ActionResult<CreatedIdResponse>> Create(CreateCategoryRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await categoryService.CreateAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.MasterData.ItemById)]
    [RequirePermission(PermissionCodes.Settings.Category.Edit)]
    public async Task<IActionResult> Update(Guid itemId, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        await categoryService.UpdateAsync(itemId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.MasterData.ItemById)]
    [RequirePermission(PermissionCodes.Settings.Category.Delete)]
    public async Task<IActionResult> Delete(Guid itemId, CancellationToken cancellationToken)
    {
        await categoryService.DeleteAsync(itemId, cancellationToken);
        return NoContent();
    }
}
