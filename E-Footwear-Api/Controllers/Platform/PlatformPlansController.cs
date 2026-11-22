using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.BLL.Platform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Platform;

/// <summary>Subscription plans and their limits. Platform administrators only.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.PlatformAdministratorOnly)]
[Route(ApiRoutes.PlatformPlans.Root)]
public sealed class PlatformPlansController(PlanAdministrationService planAdministrationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlanResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await planAdministrationService.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        await planAdministrationService.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created);
    }

    [HttpPut(ApiRoutes.PlatformPlans.Detail)]
    public async Task<IActionResult> Update(string planCode, UpdatePlanRequest request, CancellationToken cancellationToken)
    {
        await planAdministrationService.UpdateAsync(planCode, request, cancellationToken);
        return NoContent();
    }
}
