using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.BLL.Platform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Platform;

/// <summary>Platform administration: companies and their stores. Platform administrators ("super admins") only.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.PlatformAdministratorOnly)]
[Route(ApiRoutes.Platform.Root)]
public sealed class PlatformCompaniesController(PlatformService platformService) : ControllerBase
{
    [HttpGet(ApiRoutes.Platform.Companies)]
    public async Task<ActionResult<IReadOnlyList<CompanySummaryResponse>>> ListCompanies(CancellationToken cancellationToken) =>
        Ok(await platformService.ListCompaniesAsync(cancellationToken));

    [HttpPost(ApiRoutes.Platform.Companies)]
    public async Task<IActionResult> CreateCompany(CreateCompanyRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await platformService.CreateCompanyAsync(request, cancellationToken));

    [HttpGet(ApiRoutes.Platform.CompanyStores)]
    public async Task<ActionResult<IReadOnlyList<StoreResponse>>> ListStores(Guid companyId, CancellationToken cancellationToken) =>
        Ok(await platformService.ListStoresAsync(companyId, cancellationToken));

    [HttpPost(ApiRoutes.Platform.CompanyStores)]
    public async Task<IActionResult> CreateStore(Guid companyId, CreateStoreRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created,
                   new CreatedIdResponse(await platformService.CreateStoreAsync(companyId, request, cancellationToken)));
}
