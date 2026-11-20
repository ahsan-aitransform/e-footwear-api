using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Platform;

public sealed record PlatformLoginRequest(string Username, string Password);

// AccessToken looks like "admin.{secret}". Platform tokens never carry a company id.
public sealed record PlatformLoginResult(string AccessToken);

/// <summary>BLL for platform administrator sign-in. Same protections as company login: Argon2, lockout, history.</summary>
public sealed class PlatformAuthService(IPlatformAdminRepository repo, IPasswordHasher hasher, ILogger<PlatformAuthService> log)
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
            "OK" => Succeeded(outcome, req.Username, ip),
            "LOCKED" => throw Refused(ErrorKind.Locked, $"Account is locked until {outcome.LockedUntil:u}.", "locked", req.Username, ip),
            _ => throw Refused(ErrorKind.Unauthorized, "Invalid username or password.", "wrong password or unknown administrator", req.Username, ip),
        };
    }

    private PlatformLoginResult Succeeded(PlatformLoginOutcome outcome, string username, string? ip)
    {
        log.LogInformation("Platform administrator {AdminId} ({Username}) signed in (from {Ip})", outcome.AdminId, username, ip);
        return new PlatformLoginResult(TokenPrefix + outcome.SessionToken);
    }

    private DomainException Refused(ErrorKind kind, string message, string why, string username, string? ip)
    {
        log.LogWarning("Platform sign-in refused for {Username}: {Reason} (from {Ip})", username, why, ip);
        return new DomainException(kind, message);
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
