namespace EFootwear.BLL.Security.Contracts;

/// <summary>An administrator sets a temporary password; the user must change it at the next sign-in.</summary>
public sealed record ResetUserPasswordRequest(string TemporaryPassword);
