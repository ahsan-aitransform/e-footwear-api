using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>BLL contract for POS devices (core.devices). Row-level security limits every call to the caller's stores.</summary>
public interface IDeviceRepository
{
    Task<IReadOnlyList<DeviceResponse>> ListAsync(CancellationToken cancellationToken);

    Task<DeviceResponse?> FindAsync(Guid deviceId, CancellationToken cancellationToken);

    /// <summary>Creates a pending (not yet authorized) device. Only the token hash is stored.</summary>
    Task<Guid> InsertAsync(Guid companyId, Guid storeId, Guid deviceUuid, string label, string tokenHash, Guid createdByUserId,
                           CancellationToken cancellationToken);

    /// <summary>Throws a conflict when <paramref name="expectedRowVersion"/> is stale.</summary>
    Task UpdateLabelAsync(Guid companyId, Guid deviceId, string label, long expectedRowVersion, CancellationToken cancellationToken);

    Task AuthorizeAsync(Guid companyId, Guid deviceId, Guid authorizedByUserId, CancellationToken cancellationToken);

    Task RevokeAsync(Guid companyId, Guid deviceId, Guid revokedByUserId, string? reason, CancellationToken cancellationToken);

    /// <summary>Replaces the token hash (the old token stops working) and returns the device to PENDING until it is authorized again.</summary>
    Task ReplaceTokenAsync(Guid companyId, Guid deviceId, string newTokenHash, CancellationToken cancellationToken);
}
