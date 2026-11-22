using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Core.Contracts;
using EFootwear.BLL.Core.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Core;

/// <summary>The POS terminals (devices) of the stores the caller works in. Read-only; the Security module registers devices.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.PosTerminals.Root)]
public sealed class PosTerminalsController(PosTerminalService posTerminalService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Settings.DevicesAndPosTerminals.View)]
    public async Task<ActionResult<IReadOnlyList<PosTerminalResponse>>> List([FromQuery] Guid? storeId, CancellationToken cancellationToken) =>
        Ok(await posTerminalService.ListAsync(storeId, cancellationToken));
}
