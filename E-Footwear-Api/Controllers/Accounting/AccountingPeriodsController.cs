using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Accounting.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Accounting;

/// <summary>Months of the books: list, close, reopen. Closing and reopening need the Approve permission of the Accounting Periods screen.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.AccountingPeriods.Root)]
public sealed class AccountingPeriodsController(AccountingPeriodService accountingPeriodService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Voucher.AccountingPeriods.View)]
    public async Task<ActionResult<IReadOnlyList<AccountingPeriodResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await accountingPeriodService.ListAsync(cancellationToken));

    [HttpPost(ApiRoutes.AccountingPeriods.Close)]
    [RequirePermission(PermissionCodes.Voucher.AccountingPeriods.Approve)]
    public async Task<ActionResult<AccountingPeriodResponse>> Close(ChangeAccountingPeriodRequest request, CancellationToken cancellationToken) =>
        Ok(await accountingPeriodService.CloseAsync(request, cancellationToken));

    [HttpPost(ApiRoutes.AccountingPeriods.Reopen)]
    [RequirePermission(PermissionCodes.Voucher.AccountingPeriods.Approve)]
    public async Task<ActionResult<AccountingPeriodResponse>> Reopen(ChangeAccountingPeriodRequest request, CancellationToken cancellationToken) =>
        Ok(await accountingPeriodService.ReopenAsync(request, cancellationToken));
}
