using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Core.Services;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Core;

/// <summary>Store administration by the company: list, create, edit, deactivate and reactivate stores, within the plan limit.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.StoreManagement.Root)]
public sealed class StoreManagementController(StoreManagementService storeManagementService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Settings.StoreManagement.View)]
    public async Task<ActionResult<IReadOnlyList<StoreDetailResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await storeManagementService.ListAsync(cancellationToken));

    [HttpGet(ApiRoutes.StoreManagement.PlanUsage)]
    [RequirePermission(PermissionCodes.Settings.StoreManagement.View)]
    public async Task<ActionResult<PlanUsageResponse>> GetPlanUsage(CancellationToken cancellationToken) =>
        await storeManagementService.GetPlanUsageAsync(cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCodes.Settings.StoreManagement.Add)]
    public async Task<IActionResult> Create(CreateManagedStoreRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, new CreatedIdResponse(await storeManagementService.CreateAsync(request, cancellationToken)));

    [HttpPut(ApiRoutes.StoreManagement.Detail)]
    [RequirePermission(PermissionCodes.Settings.StoreManagement.Edit)]
    public async Task<IActionResult> Update(Guid storeId, UpdateManagedStoreRequest request, CancellationToken cancellationToken)
    {
        await storeManagementService.UpdateAsync(storeId, request, cancellationToken);
        return NoContent();
    }

    [HttpPost(ApiRoutes.StoreManagement.Deactivate)]
    [RequirePermission(PermissionCodes.Settings.StoreManagement.Delete)]
    public async Task<IActionResult> Deactivate(Guid storeId, CancellationToken cancellationToken)
    {
        await storeManagementService.SetActiveAsync(storeId, false, cancellationToken);
        return NoContent();
    }

    [HttpPost(ApiRoutes.StoreManagement.Activate)]
    [RequirePermission(PermissionCodes.Settings.StoreManagement.Edit)]
    public async Task<IActionResult> Activate(Guid storeId, CancellationToken cancellationToken)
    {
        await storeManagementService.SetActiveAsync(storeId, true, cancellationToken);
        return NoContent();
    }
}
