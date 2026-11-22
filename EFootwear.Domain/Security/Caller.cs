namespace EFootwear.Domain.Security;

/// <summary>Who is asking to create a user. Built by the BLL from the validated session, never from the request.</summary>
public sealed record Caller(bool IsPlatformAdministrator, string? RoleCode, bool HasAccessToAllStores,
                            IReadOnlySet<Guid> AssignedStoreIds);
