using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Platform.Services;

/// <summary>BLL for platform administrator sign-in. Same protections as company login: Argon2, lockout, history.</summary>
public sealed class PlatformAdministratorAuthenticationService(
    IPlatformAdministratorRepository administratorRepository,
    IPasswordHasher passwordHasher,
    ILogger<PlatformAdministratorAuthenticationService> logger)
{
    public async Task<PlatformLoginResponse> LoginAsync(PlatformLoginRequest request, string? clientIpAddress,
                                                        string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
            throw new DomainException(ErrorKind.Validation, "Username and password are required.");

        var storedPasswordHash = await administratorRepository.GetPasswordHashAsync(request.Username, cancellationToken);
        bool isPasswordCorrect;
        if (storedPasswordHash is null)
        {
            passwordHasher.BurnTime(request.Password);
            isPasswordCorrect = false;
        }
        else isPasswordCorrect = passwordHasher.Verify(request.Password, storedPasswordHash);

        var outcome = await administratorRepository.LoginAsync(request.Username, isPasswordCorrect, clientIpAddress,
                                                               userAgent, cancellationToken);

        return outcome.Outcome switch
        {
            LoginOutcomeCodes.Succeeded => Succeeded(outcome, request.Username, clientIpAddress),
            LoginOutcomeCodes.Locked => throw Refused(ErrorKind.Locked, $"Account is locked until {outcome.LockedUntil:u}.",
                "locked", request.Username, clientIpAddress),
            _ => throw Refused(ErrorKind.Unauthorized, "Invalid username or password.",
                "wrong password or unknown administrator", request.Username, clientIpAddress),
        };
    }

    private PlatformLoginResponse Succeeded(PlatformLoginOutcome outcome, string username, string? clientIpAddress)
    {
        logger.LogInformation("Platform administrator {AdministratorId} ({Username}) signed in (from {ClientIpAddress})",
            outcome.AdministratorId, username, clientIpAddress);
        return new PlatformLoginResponse(SessionTokenFormat.PlatformAdministratorPrefix + outcome.SessionToken);
    }

    private DomainException Refused(ErrorKind kind, string message, string reason, string username, string? clientIpAddress)
    {
        logger.LogWarning("Platform sign-in refused for {Username}: {Reason} (from {ClientIpAddress})",
            username, reason, clientIpAddress);
        return new DomainException(kind, message);
    }

    /// <summary>Strips the "admin." prefix. Returns false when the token is not a platform token.</summary>
    public static bool TryGetPlatformSecret(string rawToken, out string secret)
    {
        secret = "";
        var prefix = SessionTokenFormat.PlatformAdministratorPrefix;
        if (!rawToken.StartsWith(prefix, StringComparison.Ordinal) || rawToken.Length == prefix.Length) return false;
        secret = rawToken[prefix.Length..];
        return true;
    }

    public Task LogoutAsync(string secret, CancellationToken cancellationToken) =>
        administratorRepository.LogoutAsync(secret, cancellationToken);
}
