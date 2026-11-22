namespace EFootwear.BLL.Security.Contracts;

/// <summary>Completes a reset with the token ("{companyId}.{secret}"). The token works once and ends all sessions of the user.</summary>
public sealed record CompletePasswordResetRequest(string ResetToken, string NewPassword);
