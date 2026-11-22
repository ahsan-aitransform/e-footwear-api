using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Accounting.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.CashManagement;

/// <summary>Cash and bank account balances as on a date, per store (cash management, Part 15). Read-only; derived from the journal.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.CashAndBankBalances.Root)]
public sealed class CashAndBankBalancesController(CashAndBankBalanceService cashAndBankBalanceService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Voucher.CashAndBankBalances.View)]
    public async Task<ActionResult<CashAndBankBalancesResponse>> Get(
        [FromQuery] DateOnly asOfDate, [FromQuery] Guid? storeId, CancellationToken cancellationToken) =>
        Ok(await cashAndBankBalanceService.GetAsync(asOfDate, storeId, cancellationToken));
}
