using EFootwear.Domain.Common;

namespace EFootwear.Domain.Security;

/// <summary>
/// A registered POS device of one store. Table: core.devices. TokenHash is the SHA-256 of the device token, which is shown
/// only once when the device is registered; the token itself is never stored and TokenHash never leaves the data layer.
/// Status is derived: authorized (IsActive), revoked (RevokedAt set and not active) or pending (never authorized).
/// </summary>
public sealed class PosDevice : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public Guid DeviceUuid { get; set; }
    public string? Label { get; set; }
    public string? TokenHash { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public Guid? AuthorizedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public string? RevokeReason { get; set; }
    public long RowVersion { get; set; }

    public string Status => IsActive ? DeviceStatusCodes.Authorized
        : RevokedAt is not null ? DeviceStatusCodes.Revoked
        : DeviceStatusCodes.Pending;
}
