using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Staff.Contracts;
using EFootwear.BLL.Staff.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Staff;

/// <summary>Staff Account Detail: the ledger of one staff member or of all staff. Read only.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.StaffLedger.Root)]
public sealed class StaffLedgerController(StaffLedgerService staffLedgerService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.HumanResources.StaffAccountDetail.View)]
    public async Task<ActionResult<StaffLedgerResponse>> GetLedger(
        [FromQuery] StaffLedgerQuery query, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await staffLedgerService.GetLedgerAsync(query, pageRequest, cancellationToken));
}
