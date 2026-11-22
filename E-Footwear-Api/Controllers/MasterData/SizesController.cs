using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.BLL.MasterData.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.MasterData;

/// <summary>The sizes inside a size group (for example 38, 39, 40). They are managed on the Size Group screen, so they share its permission codes.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.MasterData.SizeGroups)]
public sealed class SizesController(SizeService sizeService) : ControllerBase
{
    [HttpGet(ApiRoutes.MasterData.SizesOfSizeGroup)]
    [RequirePermission(PermissionCodes.Settings.SizeGroup.View)]
    public async Task<ActionResult<IReadOnlyList<SizeResponse>>> List(Guid sizeGroupId, CancellationToken cancellationToken) =>
        Ok(await sizeService.ListAsync(sizeGroupId, cancellationToken));

    [HttpPost(ApiRoutes.MasterData.SizesOfSizeGroup)]
    [RequirePermission(PermissionCodes.Settings.SizeGroup.Add)]
    public async Task<ActionResult<CreatedIdResponse>> Create(Guid sizeGroupId, SaveSizeRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await sizeService.CreateAsync(sizeGroupId, request, cancellationToken));

    [HttpPut(ApiRoutes.MasterData.SizeById)]
    [RequirePermission(PermissionCodes.Settings.SizeGroup.Edit)]
    public async Task<IActionResult> Update(Guid sizeGroupId, Guid sizeId, SaveSizeRequest request, CancellationToken cancellationToken)
    {
        await sizeService.UpdateAsync(sizeGroupId, sizeId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.MasterData.SizeById)]
    [RequirePermission(PermissionCodes.Settings.SizeGroup.Delete)]
    public async Task<IActionResult> Delete(Guid sizeGroupId, Guid sizeId, CancellationToken cancellationToken)
    {
        await sizeService.DeleteAsync(sizeGroupId, sizeId, cancellationToken);
        return NoContent();
    }
}
