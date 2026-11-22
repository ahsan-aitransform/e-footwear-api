using E_Footwear_Api.Authorization;
using E_Footwear_Api.Binding;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.BLL.Platform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Platform;

/// <summary>The platform audit log: what platform administrators did. Platform administrators only.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.PlatformAdministratorOnly)]
[Route(ApiRoutes.PlatformAuditLog.Root)]
public sealed class PlatformAuditLogController(AdministratorAuditService administratorAuditService) : ControllerBase
{
    /// <summary>Paging through the shared <see cref="PageQuery"/>. The list is always newest first, so a requested sort is ignored.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdministratorAuditEntryResponse>>> List(
        [FromQuery] AdministratorAuditQuery query, [FromQuery] PageQuery pageQuery, CancellationToken cancellationToken) =>
        await administratorAuditService.ListAsync(query, pageQuery.ToPageRequest(), cancellationToken);
}
