using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Accounting.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Accounting;

/// <summary>
/// The Opening Balance screen (docx 2.18). Saving posts through the database function acct.post_opening_balance, which also checks the
/// legacy OPENING_BALANCE code, so the endpoint requires both the screen permission and that restricted operation.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.OpeningBalances.Root)]
public sealed class OpeningBalancesController(OpeningBalanceService openingBalanceService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Settings.OpeningBalance.View)]
    public async Task<ActionResult<OpeningBalanceSheetResponse>> GetSheet([FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await openingBalanceService.GetSheetAsync(storeId, cancellationToken));

    [HttpPut]
    [RequirePermission(PermissionCodes.Settings.OpeningBalance.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Accounting.OpeningBalance)]
    public async Task<ActionResult<SaveOpeningBalancesResponse>> Save(SaveOpeningBalancesRequest request, CancellationToken cancellationToken) =>
        Ok(await openingBalanceService.SaveAsync(request, cancellationToken));
}
