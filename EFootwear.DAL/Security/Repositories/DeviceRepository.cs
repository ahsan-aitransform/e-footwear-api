using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Security.Repositories;

/// <summary>
/// DAL for POS devices (core.devices). Row-level security shows only the devices of the caller's stores. Only the SHA-256 hash of the
/// token is stored; it is never read back into a response.
/// </summary>
public sealed class DeviceRepository(DatabaseSession databaseSession, IDateTimeProvider dateTimeProvider) : IDeviceRepository
{
    public async Task<IReadOnlyList<DeviceResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await (from device in databaseContext.PosDevices.AsNoTracking()
                          join store in databaseContext.Stores.AsNoTracking()
                              on new { device.CompanyId, Id = device.StoreId } equals new { store.CompanyId, store.Id }
                          orderby store.Code, device.CreatedAt
                          select new { Device = device, StoreCode = store.Code })
                         .ToListAsync(cancellationToken);
        return rows.Select(row => ToResponse(row.Device, row.StoreCode)).ToList();
    }

    public async Task<DeviceResponse?> FindAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var row = await (from device in databaseContext.PosDevices.AsNoTracking()
                         join store in databaseContext.Stores.AsNoTracking()
                             on new { device.CompanyId, Id = device.StoreId } equals new { store.CompanyId, store.Id }
                         where device.Id == deviceId
                         select new { Device = device, StoreCode = store.Code })
                        .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToResponse(row.Device, row.StoreCode);
    }

    public async Task<Guid> InsertAsync(Guid companyId, Guid storeId, Guid deviceUuid, string label, string tokenHash,
                                        Guid createdByUserId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var deviceId = Guid.CreateVersion7();
        databaseContext.PosDevices.Add(new PosDevice
        {
            CompanyId = companyId, Id = deviceId, StoreId = storeId, DeviceUuid = deviceUuid, Label = label,
            TokenHash = tokenHash, IsActive = false, CreatedBy = createdByUserId,
        });
        return deviceId;
    }

    public async Task UpdateLabelAsync(Guid companyId, Guid deviceId, string label, long expectedRowVersion,
                                       CancellationToken cancellationToken)
    {
        var device = await LoadTrackedAsync(deviceId, cancellationToken);
        (await databaseSession.GetDatabaseContextAsync(cancellationToken)).Entry(device)
            .Property(candidate => candidate.RowVersion).OriginalValue = expectedRowVersion;
        device.Label = label;
    }

    public async Task AuthorizeAsync(Guid companyId, Guid deviceId, Guid authorizedByUserId, CancellationToken cancellationToken)
    {
        var device = await LoadTrackedAsync(deviceId, cancellationToken);
        device.IsActive = true;
        device.AuthorizedAt = dateTimeProvider.UtcNow;
        device.AuthorizedBy = authorizedByUserId;
        device.RevokedAt = null;
        device.RevokedBy = null;
        device.RevokeReason = null;
    }

    public async Task RevokeAsync(Guid companyId, Guid deviceId, Guid revokedByUserId, string? reason, CancellationToken cancellationToken)
    {
        var device = await LoadTrackedAsync(deviceId, cancellationToken);
        device.IsActive = false;
        device.RevokedAt = dateTimeProvider.UtcNow;
        device.RevokedBy = revokedByUserId;
        device.RevokeReason = reason;
    }

    public async Task ReplaceTokenAsync(Guid companyId, Guid deviceId, string newTokenHash, CancellationToken cancellationToken)
    {
        var device = await LoadTrackedAsync(deviceId, cancellationToken);
        device.TokenHash = newTokenHash;
        device.IsActive = false;
        device.AuthorizedAt = null;
        device.AuthorizedBy = null;
        device.RevokedAt = null;
        device.RevokedBy = null;
        device.RevokeReason = null;
    }

    private async Task<PosDevice> LoadTrackedAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.PosDevices.SingleOrDefaultAsync(device => device.Id == deviceId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Device not found.");
    }

    private static DeviceResponse ToResponse(PosDevice device, string storeCode) =>
        new(device.Id, device.StoreId, storeCode, device.DeviceUuid, device.Label, device.Status, device.LastSeenAt, device.CreatedAt,
            device.AuthorizedAt, device.RevokedAt, device.RevokeReason, device.RowVersion);
}
