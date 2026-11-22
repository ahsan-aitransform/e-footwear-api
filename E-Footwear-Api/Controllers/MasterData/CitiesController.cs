using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.BLL.MasterData.Services;
using EFootwear.Domain.MasterData;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.MasterData;

/// <summary>
/// The master list of cities / areas (docx 2.17). Every list controller has the same six endpoints; only the route, the list definition and
/// the permission codes differ. The "options" endpoint feeds dropdowns on other screens and needs no Settings permission.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.MasterData.Cities)]
public sealed class CitiesController(MasterListService masterListService)
    : MasterListControllerBase(masterListService, MasterListDefinitions.City)
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Settings.CityArea.View)]
    public Task<ActionResult<PagedResult<MasterListItemResponse>>> List(
        [FromQuery] PageRequest pageRequest, [FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        ListItemsAsync(pageRequest, includeInactive, cancellationToken);

    [HttpGet(ApiRoutes.MasterData.Options)]
    public Task<ActionResult<IReadOnlyList<MasterListOptionResponse>>> ListOptions(CancellationToken cancellationToken) =>
        ListOptionsAsync(cancellationToken);

    [HttpGet(ApiRoutes.MasterData.ItemById)]
    [RequirePermission(PermissionCodes.Settings.CityArea.View)]
    public Task<ActionResult<MasterListItemResponse>> Get(Guid itemId, CancellationToken cancellationToken) =>
        GetItemAsync(itemId, cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCodes.Settings.CityArea.Add)]
    public Task<ActionResult<MasterListItemSavedResponse>> Create(CreateMasterListItemRequest request, CancellationToken cancellationToken) =>
        CreateItemAsync(request, cancellationToken);

    [HttpPut(ApiRoutes.MasterData.ItemById)]
    [RequirePermission(PermissionCodes.Settings.CityArea.Edit)]
    public Task<ActionResult<MasterListItemSavedResponse>> Update(Guid itemId, UpdateMasterListItemRequest request, CancellationToken cancellationToken) =>
        UpdateItemAsync(itemId, request, cancellationToken);

    [HttpDelete(ApiRoutes.MasterData.ItemById)]
    [RequirePermission(PermissionCodes.Settings.CityArea.Delete)]
    public Task<IActionResult> Delete(Guid itemId, CancellationToken cancellationToken) =>
        DeleteItemAsync(itemId, cancellationToken);
}
