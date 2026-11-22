namespace EFootwear.Tests.Infrastructure;

/// <summary>A user created through the API with a temporary password (it must be changed at the first sign-in).</summary>
public sealed record TestUser(Guid UserId, string Email, string TemporaryPassword, string RoleCode);
