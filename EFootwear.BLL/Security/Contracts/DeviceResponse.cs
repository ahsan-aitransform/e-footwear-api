namespace EFootwear.BLL.Security.Contracts;

/// <summary>A registered POS device. Status: PENDING, AUTHORIZED or REVOKED. The token hash is never returned.</summary>
public sealed record DeviceResponse(Guid Id, Guid StoreId, string StoreCode, Guid DeviceUuid, string? Label, string Status,
                                    DateTimeOffset? LastSeenAt, DateTimeOffset CreatedAt, DateTimeOffset? AuthorizedAt,
                                    DateTimeOffset? RevokedAt, string? RevokeReason, long RowVersion);
