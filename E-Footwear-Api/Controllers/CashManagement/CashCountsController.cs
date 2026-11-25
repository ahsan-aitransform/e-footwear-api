using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.CashManagement.Contracts;
using EFootwear.BLL.CashManagement.Services;
using EFootwear.BLL.Common;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.CashManagement;

/// <summary>
/// End-of-day cash counts: the Cash Calculator (docx 3.6) and View Cash Detail (docx 3.7). Saving also needs the legacy
/// CASH_COUNT restricted operation.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.CashCounts.Root)]
public sealed class CashCountsController(CashCountService cashCountService) : ControllerBase
{
    [HttpGet(ApiRoutes.CashCounts.Context)]
    [RequirePermission(PermissionCodes.CashManagement.CashCalculator.View)]
    public async Task<ActionResult<CashCountContextResponse>> GetContext([FromQuery] Guid storeId, [FromQuery] DateOnly countDate, CancellationToken cancellationToken) =>
        Ok(await cashCountService.GetContextAsync(storeId, countDate, cancellationToken));

    [HttpPut]
    [RequirePermission(PermissionCodes.CashManagement.CashCalculator.Add)]
    [RequirePermission(PermissionCodes.RestrictedOperations.Accounting.CashCount)]
    public async Task<ActionResult<CashCountResponse>> Save(SaveCashCountRequest request, CancellationToken cancellationToken) =>
        Ok(await cashCountService.SaveAsync(request, cancellationToken));

    [HttpGet]
    [RequirePermission(PermissionCodes.CashManagement.ViewCashDetail.View)]
    public async Task<ActionResult<PagedResult<CashCountListItemResponse>>> List(
        [FromQuery] CashCountSearchRequest filter, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await cashCountService.ListAsync(filter, pageRequest, cancellationToken));
}
