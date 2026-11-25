using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Customers.Contracts;
using EFootwear.BLL.Customers.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Customers;

/// <summary>Customer Account Detail: the ledger of one customer or of all customers. Read only.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.CustomerLedger.Root)]
public sealed class CustomerLedgerController(CustomerLedgerService customerLedgerService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Customers.CustomerAccountDetail.View)]
    public async Task<ActionResult<CustomerLedgerResponse>> GetLedger(
        [FromQuery] CustomerLedgerQuery query, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await customerLedgerService.GetLedgerAsync(query, pageRequest, cancellationToken));
}
