namespace EFootwear.BLL.Security.Contracts;

/// <summary>One user for the edit screen. <paramref name="RowVersion"/> goes back in the update so a stale edit is refused.</summary>
public sealed record UserDetailsResponse(
    Guid Id, string Email, string FullName, string RoleCode, bool HasAccessToAllStores, string Status,
    bool MustChangePassword, DateTimeOffset? LockedUntil, DateTimeOffset? LastSignInAt, DateTimeOffset CreatedAt,
    IReadOnlyList<Guid> StoreIds, IReadOnlyList<string> StoreCodes, long RowVersion);
