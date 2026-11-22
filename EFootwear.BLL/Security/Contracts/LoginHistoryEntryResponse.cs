namespace EFootwear.BLL.Security.Contracts;

/// <summary>One sign-in event (sec.login_attempts via sec.login_history): outcome SUCCESS, WRONG_PASSWORD, LOCKED, LOGOUT ...</summary>
public sealed record LoginHistoryEntryResponse(Guid AttemptId, Guid? UserId, string? UsernameEntered, string Outcome,
                                               DateTimeOffset AttemptedAt, string? IpAddress, string? DeviceLabel, Guid? ActedByUserId);
