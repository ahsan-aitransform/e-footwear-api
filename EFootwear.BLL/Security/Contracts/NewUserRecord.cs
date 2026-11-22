namespace EFootwear.BLL.Security.Contracts;

/// <summary>A validated new user, ready to be saved. The password is already hashed.</summary>
public sealed record NewUserRecord(string Email, string PasswordHash, string FullName, string RoleCode, bool AllStores);
