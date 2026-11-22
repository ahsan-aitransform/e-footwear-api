namespace EFootwear.Domain.Security;

/// <summary>The part of another user's account that the management rules need: who it is, its role and the stores it works in.</summary>
public sealed record ManagedUserSummary(Guid UserId, string RoleCode, IReadOnlySet<Guid> StoreIds);
