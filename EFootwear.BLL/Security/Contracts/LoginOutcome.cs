namespace EFootwear.BLL.Security.Contracts;

/// <summary>Result of sec.login. <paramref name="Outcome"/> is one of <c>LoginOutcomeCodes</c>.</summary>
public sealed record LoginOutcome(string Outcome, Guid? UserId, string? SessionToken,
                                  bool MustChangePassword, DateTimeOffset? LockedUntil);
