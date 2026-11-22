namespace EFootwear.BLL.Security.Contracts;

/// <summary>One row of the Users Account grid (docx 1.3). There is deliberately no password column.</summary>
public sealed record UserListItemResponse(
    Guid Id, string Email, string FullName, string RoleCode, bool AllStores, string Status, bool MustChangePassword,
    IReadOnlyList<Guid> StoreIds, IReadOnlyList<string> StoreCodes,
    DateTimeOffset? LastSignInAt, bool IsSignedInNow, DateTimeOffset? LockedUntil);
