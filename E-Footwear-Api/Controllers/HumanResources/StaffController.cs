using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.HumanResources.Contracts;
using EFootwear.BLL.HumanResources.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.HumanResources;

/// <summary>
/// Staff profiles with their salary information, the salary history and the store history of each staff member, and the company-wide list of staff
/// transfers (Part 16.1). A staff member is created and edited under <c>/staff-members</c>; moving one to another store is <c>POST /staff-members/{id}/transfers</c>.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Staff.Root)]
public sealed class StaffController(StaffDirectoryService staffDirectoryService, StaffSalaryService staffSalaryService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.HumanResources.StaffProfiles.View)]
    public async Task<ActionResult<PagedResult<StaffListItemResponse>>> List(
        [FromQuery] StaffSearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await staffDirectoryService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.Staff.Transfers)]
    [RequirePermission(PermissionCodes.HumanResources.StaffTransfers.View)]
    public async Task<ActionResult<PagedResult<StaffTransferListItemResponse>>> ListTransfers(
        [FromQuery] StaffTransferSearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await staffDirectoryService.SearchTransfersAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.Staff.StaffById)]
    [RequirePermission(PermissionCodes.HumanResources.StaffProfiles.View)]
    public async Task<ActionResult<StaffDetailResponse>> Get(Guid staffId, CancellationToken cancellationToken) =>
        Ok(await staffDirectoryService.GetAsync(staffId, cancellationToken));

    [HttpPut(ApiRoutes.Staff.SalaryTerms)]
    [RequirePermission(PermissionCodes.HumanResources.SalaryTerms.Edit)]
    public async Task<IActionResult> UpdateSalaryTerms(Guid staffId, UpdateStaffSalaryTermsRequest request, CancellationToken cancellationToken)
    {
        await staffSalaryService.UpdateSalaryTermsAsync(staffId, request, cancellationToken);
        return NoContent();
    }
}
