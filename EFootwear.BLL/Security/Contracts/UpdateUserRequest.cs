namespace EFootwear.BLL.Security.Contracts;

/// <summary>Edit User (docx 1.3 Edit). Status, password and permissions have their own actions.</summary>
public sealed record UpdateUserRequest(string Email, string FullName, string RoleCode, IReadOnlyList<Guid>? StoreIds, long RowVersion);
