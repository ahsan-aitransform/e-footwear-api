namespace EFootwear.BLL.Platform.Contracts;

/// <summary>Result of platform.admin_login. <paramref name="Outcome"/> is one of <c>LoginOutcomeCodes</c>.</summary>
public sealed record PlatformLoginOutcome(string Outcome, Guid? AdministratorId, string? SessionToken,
                                          DateTimeOffset? LockedUntil);
