using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// POS device registration and authorization (Part 6 device authorization, Part 23.7 device registration). Flow:
///  register (a device of a store, pending, with a token shown ONCE and stored only as a hash)
///  -> authorize (needs the Approve permission) -> revoke at any time (lost or stolen device) -> or issue a new token.
/// Only the stores the caller works in are visible: row-level security limits core.devices, and every write also checks the store.
/// What a device may do once authorized (offline synchronization) is built in the offline module; this service owns its identity.
/// </summary>
public sealed class DeviceManagementService(
    IDeviceRepository deviceRepository,
    IStoreRepository storeRepository,
    IDeviceTokenService deviceTokenService,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<DeviceManagementService> logger)
{
    public Task<IReadOnlyList<DeviceResponse>> ListAsync(CancellationToken cancellationToken) =>
        deviceRepository.ListAsync(cancellationToken);

    public async Task<DeviceResponse> GetAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await FindAccessibleDeviceAsync(deviceId, cancellationToken);

    public async Task<RegisteredDeviceResponse> RegisterAsync(RegisterDeviceRequest request, CancellationToken cancellationToken)
    {
        var label = SecurityLimits.EnsureRequiredText(request.Label, "Label", SecurityLimits.MaximumDeviceLabelLength);
        if (request.StoreId == Guid.Empty || !requestContext.CanAccessStore(request.StoreId))
            throw new DomainException(ErrorKind.Forbidden, "Choose one of your own stores for the device.");
        var validStoreIds = await storeRepository.ActiveSellingStoreIdsAsync(requestContext.CompanyId, [request.StoreId], cancellationToken);
        if (validStoreIds.Count != 1)
            throw new DomainException(ErrorKind.Validation, "The store is not valid for this company.");

        var deviceUuid = request.DeviceUuid is { } given && given != Guid.Empty ? given : Guid.CreateVersion7();
        var token = deviceTokenService.Generate();
        var deviceId = await deviceRepository.InsertAsync(requestContext.CompanyId, request.StoreId, deviceUuid, label, token.TokenHash,
                                                          requestContext.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Device {DeviceId} registered for store {StoreId} by {ActorUserId}", deviceId, request.StoreId, requestContext.UserId);

        var device = await deviceRepository.FindAsync(deviceId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Device not found.");
        return new RegisteredDeviceResponse(device, token.Token);
    }

    public async Task UpdateLabelAsync(Guid deviceId, UpdateDeviceRequest request, CancellationToken cancellationToken)
    {
        var label = SecurityLimits.EnsureRequiredText(request.Label, "Label", SecurityLimits.MaximumDeviceLabelLength);
        await FindAccessibleDeviceAsync(deviceId, cancellationToken);
        await deviceRepository.UpdateLabelAsync(requestContext.CompanyId, deviceId, label, request.RowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task AuthorizeAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await FindAccessibleDeviceAsync(deviceId, cancellationToken);
        if (device.Status == DeviceStatusCodes.Authorized)
            throw new DomainException(ErrorKind.Conflict, "The device is already authorized.");
        await deviceRepository.AuthorizeAsync(requestContext.CompanyId, deviceId, requestContext.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Device {DeviceId} authorized by {ActorUserId}", deviceId, requestContext.UserId);
    }

    public async Task RevokeAsync(Guid deviceId, RevokeDeviceRequest request, CancellationToken cancellationToken)
    {
        var reason = SecurityLimits.EnsureOptionalText(request.Reason, "Reason", SecurityLimits.MaximumReasonLength);
        var device = await FindAccessibleDeviceAsync(deviceId, cancellationToken);
        if (device.Status == DeviceStatusCodes.Revoked)
            throw new DomainException(ErrorKind.Conflict, "The device is already revoked.");
        await deviceRepository.RevokeAsync(requestContext.CompanyId, deviceId, requestContext.UserId, reason, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogWarning("Device {DeviceId} revoked by {ActorUserId}", deviceId, requestContext.UserId);
    }

    /// <summary>Gives the device a new token (the old one stops working at once). The device is pending again until it is authorized.</summary>
    public async Task<RegisteredDeviceResponse> RotateTokenAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await FindAccessibleDeviceAsync(deviceId, cancellationToken);
        var token = deviceTokenService.Generate();
        await deviceRepository.ReplaceTokenAsync(requestContext.CompanyId, deviceId, token.TokenHash, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Device {DeviceId} received a new token from {ActorUserId}", deviceId, requestContext.UserId);

        var device = await deviceRepository.FindAsync(deviceId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Device not found.");
        return new RegisteredDeviceResponse(device, token.Token);
    }

    private async Task<DeviceResponse> FindAccessibleDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await deviceRepository.FindAsync(deviceId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Device not found.");
        if (!requestContext.CanAccessStore(device.StoreId))
            throw new DomainException(ErrorKind.NotFound, "Device not found.");
        return device;
    }
}
