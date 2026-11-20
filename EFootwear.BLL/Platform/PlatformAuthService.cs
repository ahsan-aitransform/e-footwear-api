using EFootwear.BLL.Abstractions;
using EFootwear.Domain.Common;

namespace EFootwear.BLL.Platform;

public sealed record PlatformLoginRequest(string Username, string Password);

// AccessToken looks like "admin.{secret}". Platform tokens never carry a company id.
public sealed record PlatformLoginResult(string AccessToken);

/// <summary>BLL for platform administrator sign-in. Same protections as company login: Argon2, lockout, history.</summary>
public sealed class PlatformAuthService(IPlatformAdminRepository repo, IPasswordHasher hasher)
{
    public const string TokenPrefix = "admin.";

    public async Task<PlatformLoginResult> LoginAsync(PlatformLoginRequest req, string? ip, string? userAgent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrEmpty(req.Password))
            throw new DomainException(ErrorKind.Validation, "Username and password are required.");

        var hash = await repo.GetPasswordHashAsync(req.Username, ct);
        bool ok;
        if (hash is null) { hasher.BurnTime(req.Password); ok = false; }
        else ok = hasher.Verify(req.Password, hash);

        var outcome = await repo.LoginAsync(req.Username, ok, ip, userAgent, ct);

        return outcome.Outcome switch
        {
            "OK" => new PlatformLoginResult(TokenPrefix + outcome.SessionToken),
            "LOCKED" => throw new DomainException(ErrorKind.Locked, $"Account is locked until {outcome.LockedUntil:u}."),
            _ => throw new DomainException(ErrorKind.Unauthorized, "Invalid username or password."),
        };
    }

    /// <summary>Strips the "admin." prefix. Returns false when the token is not a platform token.</summary>
    public static bool TryGetPlatformSecret(string raw, out string secret)
    {
        secret = "";
        if (!raw.StartsWith(TokenPrefix, StringComparison.Ordinal) || raw.Length == TokenPrefix.Length) return false;
        secret = raw[TokenPrefix.Length..];
        return true;
    }

    public Task LogoutAsync(string secret, CancellationToken ct) => repo.LogoutAsync(secret, ct);
}
