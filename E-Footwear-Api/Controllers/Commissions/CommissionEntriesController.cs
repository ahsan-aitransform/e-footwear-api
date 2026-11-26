using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Commissions.Contracts;
using EFootwear.BLL.Commissions.Services;
using EFootwear.BLL.Common;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Commissions;

/// <summary>
/// The commission entries and their approval (Part 16.2). Approving needs the Approve action of the Commission Entries screen and the legacy code
/// COMMISSION_APPROVE, because the database function checks that code again. Entries cannot be created or edited here: the database makes them when a sale is posted.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Commissions.Root)]
public sealed class CommissionEntriesController(CommissionEntryService commissionEntryService) : ControllerBase
{
    [HttpGet(ApiRoutes.Commissions.Entries)]
    [RequirePermission(PermissionCodes.Commissions.CommissionEntries.View)]
    public async Task<ActionResult<PagedResult<CommissionEntryResponse>>> Search(
        [FromQuery] CommissionEntrySearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await commissionEntryService.SearchAsync(filter, pageRequest, cancellationToken));

    [HttpGet(ApiRoutes.Commissions.EntryTotals)]
    [RequirePermission(PermissionCodes.Commissions.CommissionEntries.View)]
    public async Task<ActionResult<CommissionEntryTotalsResponse>> GetTotals([FromQuery] CommissionEntrySearchRequest filter, CancellationToken cancellationToken) =>
        Ok(await commissionEntryService.GetTotalsAsync(filter, cancellationToken));

    [HttpPost(ApiRoutes.Commissions.ApproveEntries)]
    [RequirePermission(PermissionCodes.Commissions.CommissionEntries.Approve)]
    [RequirePermission(PermissionCodes.RestrictedOperations.ExpensesAndPayroll.CommissionApprove)]
    public async Task<ActionResult<ApproveCommissionsResponse>> Approve(ApproveCommissionsRequest request, CancellationToken cancellationToken) =>
        Ok(await commissionEntryService.ApproveAsync(request, cancellationToken));
}
