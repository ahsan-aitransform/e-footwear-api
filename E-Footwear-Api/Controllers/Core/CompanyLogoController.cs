using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Core.Services;
using EFootwear.Domain.Common;
using EFootwear.Domain.Core;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Core;

/// <summary>
/// The company logo. Uploading needs the company-profile Edit permission. Reading the picture needs no screen permission: every
/// signed-in user of the company sees the logo in the header and on printed documents. The file is only ever served to its own company.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.CompanySettings.Root)]
public sealed class CompanyLogoController(CompanyLogoService companyLogoService) : ControllerBase
{
    // A little more than the logo limit, for the multipart envelope; the service enforces the exact limit on the file itself.
    private const long RequestSizeLimitBytes = AttachmentRules.LogoMaximumSizeBytes + 64 * 1024;

    [HttpPut(ApiRoutes.CompanySettings.CompanyLogo)]
    [RequirePermission(PermissionCodes.Settings.CompanyProfile.Edit)]
    [RequestSizeLimit(RequestSizeLimitBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<CompanyLogoResponse>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        return await companyLogoService.UploadAsync(content, cancellationToken);
    }

    /// <summary>Whether the company has a logo and which one (204 when it has none).</summary>
    [HttpGet(ApiRoutes.CompanySettings.CompanyLogoInformation)]
    public async Task<ActionResult<CompanyLogoResponse>> GetInformation(CancellationToken cancellationToken)
    {
        var logo = await companyLogoService.GetCurrentAsync(cancellationToken);
        return logo is null ? NoContent() : logo;
    }

    [HttpGet(ApiRoutes.CompanySettings.CompanyLogo)]
    public async Task<IActionResult> GetContent(CancellationToken cancellationToken)
    {
        var logo = await companyLogoService.OpenCurrentAsync(cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The company has no logo.");
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(logo.Content, logo.ContentType);
    }
}
