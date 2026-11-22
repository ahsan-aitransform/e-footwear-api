using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Parties.Contracts;
using EFootwear.BLL.Parties.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Parties;

/// <summary>
/// Parties: New Party (docx 3.1), Manage Party (3.2) and Search Party (3.3), plus staff transfers between stores. Manage Party and
/// Search Party are two screens with their own permission, so each has its own list endpoint over the same use case.
/// The "lookup" endpoint gives a light list for dropdowns on other screens (a customer on a sale) to every company user.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Parties.Root)]
public sealed class PartiesController(PartyService partyService, StaffTransferService staffTransferService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Party.ViewParty.View)]
    public async Task<ActionResult<PagedResult<PartyListItemResponse>>> List(
        [FromQuery] PartySearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await partyService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.Parties.Search)]
    [RequirePermission(PermissionCodes.Party.SearchParty.View)]
    public async Task<ActionResult<PagedResult<PartyListItemResponse>>> Search(
        [FromQuery] PartySearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await partyService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.Parties.Lookup)]
    public async Task<ActionResult<IReadOnlyList<PartyLookupItemResponse>>> Lookup(
        [FromQuery] string? partyTypeCode, [FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await partyService.LookupAsync(partyTypeCode, searchText, cancellationToken));

    [HttpGet(ApiRoutes.Parties.PartyById)]
    [RequirePermission(PermissionCodes.Party.NewParty.View)]
    public async Task<ActionResult<PartyDetailResponse>> Get(Guid partyId, CancellationToken cancellationToken) =>
        Ok(await partyService.GetAsync(partyId, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.Party.NewParty.Add)]
    public async Task<ActionResult<CreatedIdResponse>> Create(CreatePartyRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await partyService.CreateAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.Parties.PartyById)]
    [RequirePermission(PermissionCodes.Party.NewParty.Edit)]
    public async Task<IActionResult> Update(Guid partyId, UpdatePartyRequest request, CancellationToken cancellationToken)
    {
        await partyService.UpdateAsync(partyId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.Parties.PartyById)]
    [RequirePermission(PermissionCodes.Party.ViewParty.Delete)]
    public async Task<IActionResult> Delete(Guid partyId, CancellationToken cancellationToken)
    {
        await partyService.DeleteAsync(partyId, cancellationToken);
        return NoContent();
    }

    [HttpGet(ApiRoutes.Parties.StaffTransfers)]
    [RequirePermission(PermissionCodes.HumanResources.StaffTransfers.View)]
    public async Task<ActionResult<IReadOnlyList<StaffStoreHistoryResponse>>> ListStaffTransfers(Guid partyId, CancellationToken cancellationToken) =>
        Ok(await staffTransferService.ListHistoryAsync(partyId, cancellationToken));

    [HttpPost(ApiRoutes.Parties.StaffTransfers)]
    [RequirePermission(PermissionCodes.HumanResources.StaffTransfers.Add)]
    public async Task<IActionResult> TransferStaff(Guid partyId, TransferStaffRequest request, CancellationToken cancellationToken)
    {
        await staffTransferService.TransferAsync(partyId, request, cancellationToken);
        return NoContent();
    }
}
