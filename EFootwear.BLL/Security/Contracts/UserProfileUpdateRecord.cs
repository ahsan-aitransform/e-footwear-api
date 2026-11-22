namespace EFootwear.BLL.Security.Contracts;

/// <summary>A validated change of a user's name, email and role, ready to be saved.</summary>
public sealed record UserProfileUpdateRecord(string Email, string FullName, string RoleCode, bool HasAccessToAllStores);
