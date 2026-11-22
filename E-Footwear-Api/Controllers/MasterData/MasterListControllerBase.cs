using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.BLL.MasterData.Services;
using EFootwear.Domain.MasterData;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.MasterData;

/// <summary>
/// The work every simple master list controller does. A list controller (DepartmentsController, BrandsController ...) is a thin
/// class that declares its own route, its own permission codes on every action, and calls these helpers with its
/// <see cref="MasterListDefinition"/>. The permission attributes stay on the concrete controllers so the architecture test and
/// the OpenAPI document see every endpoint; there are no business rules here.
/// </summary>
public abstract class MasterListControllerBase(MasterListService masterListService, MasterListDefinition definition) : ControllerBase
{
    protected async Task<ActionResult<PagedResult<MasterListItemResponse>>> ListItemsAsync(PageRequest pageRequest, bool includeInactive, CancellationToken cancellationToken) =>
        Ok(await masterListService.ListAsync(definition, pageRequest, includeInactive, cancellationToken));

    protected async Task<ActionResult<IReadOnlyList<MasterListOptionResponse>>> ListOptionsAsync(CancellationToken cancellationToken) =>
        Ok(await masterListService.ListOptionsAsync(definition, cancellationToken));

    protected async Task<ActionResult<MasterListItemResponse>> GetItemAsync(Guid itemId, CancellationToken cancellationToken) =>
        Ok(await masterListService.GetAsync(definition, itemId, cancellationToken));

    protected async Task<ActionResult<MasterListItemSavedResponse>> CreateItemAsync(CreateMasterListItemRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await masterListService.CreateAsync(definition, request, cancellationToken));

    protected async Task<ActionResult<MasterListItemSavedResponse>> UpdateItemAsync(Guid itemId, UpdateMasterListItemRequest request, CancellationToken cancellationToken) =>
        Ok(await masterListService.UpdateAsync(definition, itemId, request, cancellationToken));

    protected async Task<IActionResult> DeleteItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await masterListService.DeleteAsync(definition, itemId, cancellationToken);
        return NoContent();
    }
}
