using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Security.Contracts;
using EFootwear.BLL.Security.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Security;

/// <summary>
/// POS devices (Part 6 device authorization): register a device for a store, authorize it, revoke it, give it a new token. A device token
/// is returned once, when it is created, and only its hash is stored.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.Devices.Root)]
public sealed class DevicesController(DeviceManagementService deviceManagementService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Settings.DevicesAndPosTerminals.View)]
    public async Task<ActionResult<IReadOnlyList<DeviceResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await deviceManagementService.ListAsync(cancellationToken));

    [HttpGet(ApiRoutes.Devices.ById)]
    [RequirePermission(PermissionCodes.Settings.DevicesAndPosTerminals.View)]
    public async Task<ActionResult<DeviceResponse>> Get(Guid deviceId, CancellationToken cancellationToken) =>
        await deviceManagementService.GetAsync(deviceId, cancellationToken);

    [HttpPost]
    [RequirePermission(PermissionCodes.Settings.DevicesAndPosTerminals.Add)]
    public async Task<ActionResult<RegisteredDeviceResponse>> Register(RegisterDeviceRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await deviceManagementService.RegisterAsync(request, cancellationToken));

    [HttpPut(ApiRoutes.Devices.ById)]
    [RequirePermission(PermissionCodes.Settings.DevicesAndPosTerminals.Edit)]
    public async Task<IActionResult> UpdateLabel(Guid deviceId, UpdateDeviceRequest request, CancellationToken cancellationToken)
    {
        await deviceManagementService.UpdateLabelAsync(deviceId, request, cancellationToken);
        return NoContent();
    }

    [HttpPost(ApiRoutes.Devices.Authorize)]
    [RequirePermission(PermissionCodes.Settings.DevicesAndPosTerminals.Approve)]
    public async Task<IActionResult> Authorize(Guid deviceId, CancellationToken cancellationToken)
    {
        await deviceManagementService.AuthorizeAsync(deviceId, cancellationToken);
        return NoContent();
    }

    [HttpPost(ApiRoutes.Devices.Revoke)]
    [RequirePermission(PermissionCodes.Settings.DevicesAndPosTerminals.Approve)]
    public async Task<IActionResult> Revoke(Guid deviceId, RevokeDeviceRequest request, CancellationToken cancellationToken)
    {
        await deviceManagementService.RevokeAsync(deviceId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>A new token; the old one stops working at once and the device must be authorized again.</summary>
    [HttpPost(ApiRoutes.Devices.RotateToken)]
    [RequirePermission(PermissionCodes.Settings.DevicesAndPosTerminals.Edit)]
    public async Task<ActionResult<RegisteredDeviceResponse>> RotateToken(Guid deviceId, CancellationToken cancellationToken) =>
        await deviceManagementService.RotateTokenAsync(deviceId, cancellationToken);
}
