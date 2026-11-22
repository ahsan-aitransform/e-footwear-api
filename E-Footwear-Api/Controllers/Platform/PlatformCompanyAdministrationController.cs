using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.BLL.Platform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Platform;

/// <summary>One company as the platform sees it: profile and plan usage, plan assignment (subscription) and status. Platform administrators only.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.PlatformAdministratorOnly)]
[Route(ApiRoutes.PlatformCompanyAdministration.Root)]
public sealed class PlatformCompanyAdministrationController(CompanyAdministrationService companyAdministrationService) : ControllerBase
{
    [HttpGet(ApiRoutes.PlatformCompanyAdministration.Detail)]
    public async Task<ActionResult<CompanyDetailResponse>> GetDetail(Guid companyId, CancellationToken cancellationToken) =>
        await companyAdministrationService.GetDetailAsync(companyId, cancellationToken);

    [HttpPut(ApiRoutes.PlatformCompanyAdministration.Plan)]
    public async Task<IActionResult> AssignPlan(Guid companyId, AssignCompanyPlanRequest request, CancellationToken cancellationToken)
    {
        await companyAdministrationService.AssignPlanAsync(companyId, request, cancellationToken);
        return NoContent();
    }

    [HttpPost(ApiRoutes.PlatformCompanyAdministration.Status)]
    public async Task<IActionResult> SetStatus(Guid companyId, SetCompanyStatusRequest request, CancellationToken cancellationToken)
    {
        await companyAdministrationService.SetStatusAsync(companyId, request, cancellationToken);
        return NoContent();
    }
}
