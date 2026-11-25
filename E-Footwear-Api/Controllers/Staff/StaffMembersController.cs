using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Staff.Contracts;
using EFootwear.BLL.Staff.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Staff;

/// <summary>
/// Staff: New Staff, View Staff and Search Staff, plus staff transfers between stores. View and Search are two screens with their own permission, so
/// each has its own list endpoint over the same use case. The "lookup" endpoint gives a light list for dropdowns on other screens to every company user.
/// Salary terms and the staff profile page with the salary history live under <c>/staff</c> (Human Resources).
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.StaffMembers.Root)]
public sealed class StaffMembersController(StaffMemberService staffMemberService, StaffTransferService staffTransferService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.HumanResources.ViewStaff.View)]
    public async Task<ActionResult<PagedResult<StaffMemberListItemResponse>>> List(
        [FromQuery] StaffMemberSearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await staffMemberService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.StaffMembers.Search)]
    [RequirePermission(PermissionCodes.HumanResources.SearchStaff.View)]
    public async Task<ActionResult<PagedResult<StaffMemberListItemResponse>>> Search(
        [FromQuery] StaffMemberSearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await staffMemberService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.StaffMembers.Lookup)]
    public async Task<ActionResult<IReadOnlyList<StaffMemberLookupItemResponse>>> Lookup([FromQuery] string? searchText, CancellationToken cancellationToken) =>
        Ok(await staffMemberService.LookupAsync(searchText, cancellationToken));

    [HttpGet(ApiRoutes.StaffMembers.StaffById)]
    [RequirePermission(PermissionCodes.HumanResources.NewStaff.View)]
    public async Task<ActionResult<StaffMemberDetailResponse>> Get(Guid staffId, CancellationToken cancellationToken) =>
        Ok(await staffMemberService.GetAsync(staffId, cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.HumanResources.NewStaff.Add)]
    public async Task<ActionResult<CreatedIdResponse>> Create(CreateStaffMemberRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await staffMemberService.CreateAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.StaffMembers.StaffById)]
    [RequirePermission(PermissionCodes.HumanResources.NewStaff.Edit)]
    public async Task<IActionResult> Update(Guid staffId, UpdateStaffMemberRequest request, CancellationToken cancellationToken)
    {
        await staffMemberService.UpdateAsync(staffId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete(ApiRoutes.StaffMembers.StaffById)]
    [RequirePermission(PermissionCodes.HumanResources.ViewStaff.Delete)]
    public async Task<IActionResult> Delete(Guid staffId, CancellationToken cancellationToken)
    {
        await staffMemberService.DeleteAsync(staffId, cancellationToken);
        return NoContent();
    }

    [HttpGet(ApiRoutes.StaffMembers.Transfers)]
    [RequirePermission(PermissionCodes.HumanResources.StaffTransfers.View)]
    public async Task<ActionResult<IReadOnlyList<StaffStoreHistoryResponse>>> ListTransfers(Guid staffId, CancellationToken cancellationToken) =>
        Ok(await staffTransferService.ListHistoryAsync(staffId, cancellationToken));

    [HttpPost(ApiRoutes.StaffMembers.Transfers)]
    [RequirePermission(PermissionCodes.HumanResources.StaffTransfers.Add)]
    public async Task<IActionResult> Transfer(Guid staffId, TransferStaffRequest request, CancellationToken cancellationToken)
    {
        await staffTransferService.TransferAsync(staffId, request, cancellationToken);
        return NoContent();
    }
}
