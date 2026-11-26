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
/// Commission plans (Part 16.2): who earns commission, on what, at which rate and from when to when, limited to chosen staff or open to a whole role, with
/// rules per product, category or price band. Every action has its own permission of the Commission Plans screen.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Commissions.Root)]
public sealed class CommissionPlansController(CommissionPlanService commissionPlanService) : ControllerBase
{
    [HttpGet(ApiRoutes.Commissions.Plans)]
    [RequirePermission(PermissionCodes.Commissions.CommissionPlans.View)]
    public async Task<ActionResult<IReadOnlyList<CommissionPlanResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await commissionPlanService.ListAsync(cancellationToken));

    [HttpGet(ApiRoutes.Commissions.PlanById)]
    [RequirePermission(PermissionCodes.Commissions.CommissionPlans.View)]
    public async Task<ActionResult<CommissionPlanResponse>> Get(Guid planId, CancellationToken cancellationToken) =>
        Ok(await commissionPlanService.GetAsync(planId, cancellationToken));

    [HttpPost(ApiRoutes.Commissions.Plans)]
    [RequirePermission(PermissionCodes.Commissions.CommissionPlans.Add)]
    public async Task<ActionResult<CommissionPlanResponse>> Create(SaveCommissionPlanRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await commissionPlanService.CreateAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.Commissions.PlanById)]
    [RequirePermission(PermissionCodes.Commissions.CommissionPlans.Edit)]
    public async Task<ActionResult<CommissionPlanResponse>> Update(Guid planId, SaveCommissionPlanRequest request, CancellationToken cancellationToken) =>
        Ok(await commissionPlanService.UpdateAsync(planId, request, cancellationToken));

    [HttpDelete(ApiRoutes.Commissions.PlanById)]
    [RequirePermission(PermissionCodes.Commissions.CommissionPlans.Delete)]
    public async Task<IActionResult> Delete(Guid planId, CancellationToken cancellationToken)
    {
        await commissionPlanService.DeleteAsync(planId, cancellationToken);
        return NoContent();
    }

    [HttpPost(ApiRoutes.Commissions.PlanRules)]
    [RequirePermission(PermissionCodes.Commissions.CommissionPlans.Edit)]
    public async Task<ActionResult<CommissionPlanResponse>> AddRule(Guid planId, SaveCommissionRuleRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await commissionPlanService.AddRuleAsync(planId, request, cancellationToken));

    [HttpPost(ApiRoutes.Commissions.EndPlanRule)]
    [RequirePermission(PermissionCodes.Commissions.CommissionPlans.Edit)]
    public async Task<ActionResult<CommissionPlanResponse>> EndRule(Guid planId, Guid ruleId, EndCommissionRuleRequest request, CancellationToken cancellationToken) =>
        Ok(await commissionPlanService.EndRuleAsync(planId, ruleId, request, cancellationToken));
}
