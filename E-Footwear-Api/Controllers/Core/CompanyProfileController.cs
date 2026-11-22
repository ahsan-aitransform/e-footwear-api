using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Core.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Core;

/// <summary>The legal and regional profile of the signed-in user's own company (time zone, currency, NTN, STRN, rounding).</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.CompanySettings.Root + "/" + ApiRoutes.CompanySettings.CompanyProfile)]
public sealed class CompanyProfileController(CompanyProfileService companyProfileService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Settings.CompanyProfile.View)]
    public async Task<ActionResult<CompanyProfileResponse>> Get(CancellationToken cancellationToken) =>
        await companyProfileService.GetAsync(cancellationToken);

    [HttpPut]
    [RequirePermission(PermissionCodes.Settings.CompanyProfile.Edit)]
    public async Task<ActionResult<CompanyProfileResponse>> Update(UpdateCompanyProfileRequest request, CancellationToken cancellationToken) =>
        await companyProfileService.UpdateAsync(request, cancellationToken);
}
