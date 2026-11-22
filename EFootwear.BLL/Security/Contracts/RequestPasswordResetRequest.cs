namespace EFootwear.BLL.Security.Contracts;

/// <summary>Forgot password: the answer is always the same, whether or not the email has an account.</summary>
public sealed record RequestPasswordResetRequest(string Email);
