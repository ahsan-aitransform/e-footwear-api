namespace EFootwear.BLL.Security.Contracts;

/// <summary>The display data of the signed-in company user, for GET /auth/me.</summary>
public sealed record CurrentUserProfile(string FullName, string Email, string CompanyName);
