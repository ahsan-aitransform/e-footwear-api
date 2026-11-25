using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Vendors.Contracts;
using EFootwear.BLL.Vendors.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Vendors;

/// <summary>Vendor Account Detail: the ledger of one vendor or of all vendors. Read only.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.VendorLedger.Root)]
public sealed class VendorLedgerController(VendorLedgerService vendorLedgerService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Vendors.VendorAccountDetail.View)]
    public async Task<ActionResult<VendorLedgerResponse>> GetLedger(
        [FromQuery] VendorLedgerQuery query, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await vendorLedgerService.GetLedgerAsync(query, pageRequest, cancellationToken));
}
