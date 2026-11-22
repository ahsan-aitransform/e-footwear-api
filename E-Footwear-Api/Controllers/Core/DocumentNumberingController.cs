using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Core.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Core;

/// <summary>Document numbering: where each series of a store continues. A series can only be moved forward.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.DocumentNumbering.Root)]
public sealed class DocumentNumberingController(DocumentNumberingService documentNumberingService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Settings.DocumentNumbering.View)]
    public async Task<ActionResult<IReadOnlyList<DocumentNumberSeriesResponse>>> List(
        [FromQuery] Guid storeId, CancellationToken cancellationToken) =>
        Ok(await documentNumberingService.ListAsync(storeId, cancellationToken));

    [HttpPut]
    [RequirePermission(PermissionCodes.Settings.DocumentNumbering.Edit)]
    public async Task<IActionResult> SetNextValue(SetDocumentNumberRequest request, CancellationToken cancellationToken)
    {
        await documentNumberingService.SetNextValueAsync(request, cancellationToken);
        return NoContent();
    }
}
