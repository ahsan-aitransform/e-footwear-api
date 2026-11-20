using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Auth;

public sealed record LoginRequest(string Email, string Password);

// AccessToken looks like "{companyId}.{secret}". The client sends it back on every request
// as "Authorization: Bearer <AccessToken>".
public sealed record LoginResult(string AccessToken, bool MustChangePassword);

/// <summary>
/// BLL (Business Logic Layer) for signing in and out. This class decides WHAT happens; it never touches
/// SQL or HTTP directly. It talks to the database only through <see cref="IAuthRepository"/>,
/// so it can be unit-tested with a fake repository.
/// </summary>
public sealed class AuthService(IAuthRepository repo, IPasswordHasher hasher, IUnitOfWork uow, ILogger<AuthService> log)
{
    /// <summary>
    /// Login steps:
    ///  1. Check the input is filled in.
    ///  2. Fetch the stored password hash for that user (inside the given company only).
    ///  3. Verify the typed password against the hash (Argon2id) in C#.
    ///  4. Tell the database the result. The database then applies lockout rules, counts failed
    ///     attempts, writes the login history and creates the session token.
    ///  5. Commit. A FAILED attempt is committed too, otherwise the failure counter and history
    ///     would be rolled back and lockout would never trigger.
    /// </summary>
    public async Task<LoginResult> LoginAsync(LoginRequest req, string? ip, string? userAgent, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password))
            throw new DomainException(ErrorKind.Validation, "Email and password are required.");

        // The email is unique across all companies, so it alone tells us which company to look in.
        var email = req.Email.Trim().ToLowerInvariant();
        if (await repo.FindCompanyIdByEmailAsync(email, ct) is not { } companyId)
        {
            // Unknown email: same answer, and same time, as a wrong password.
            hasher.BurnTime(req.Password);
            log.LogWarning("Sign-in refused: no account for {Email} (from {Ip})", LogMask.Email(email), ip);
            throw new DomainException(ErrorKind.Unauthorized, "Invalid email or password.");
        }

        // Before login nobody is signed in, so we tell the database which company to look in.
        repo.UseCompany(companyId);

        var hash = await repo.GetPasswordHashAsync(email, ct);
        bool ok;
        if (hash is null)
        {
            // Unknown user: do a fake password check anyway so the response time does not reveal
            // whether the username exists.
            hasher.BurnTime(req.Password);
            ok = false;
        }
        else ok = hasher.Verify(req.Password, hash);

        var outcome = await repo.LoginAsync(companyId, email, ok, ip, userAgent, ct);
        await uow.CommitAsync(ct);

        switch (outcome.Outcome)
        {
            case "OK":
                log.LogInformation("Sign-in succeeded: user {UserId} of company {CompanyId} (from {Ip})",
                    outcome.UserId, companyId, ip);
                return new LoginResult($"{companyId}.{outcome.SessionToken}", outcome.MustChangePassword);
            case "LOCKED":
                log.LogWarning("Sign-in refused: account {Email} of company {CompanyId} is locked until {LockedUntil:u} (from {Ip})",
                    LogMask.Email(email), companyId, outcome.LockedUntil, ip);
                throw new DomainException(ErrorKind.Locked, $"Account is locked until {outcome.LockedUntil:u}.");
            default:
                // Wrong password, unknown user and disabled user all give the SAME message on purpose.
                log.LogWarning("Sign-in failed: wrong password, or blocked account, for {Email} of company {CompanyId} (from {Ip})",
                    LogMask.Email(email), companyId, ip);
                throw new DomainException(ErrorKind.Unauthorized, "Invalid email or password.");
        }
    }

    /// <summary>
    /// Changes the signed-in user's own password. The current password must be right. The database then
    /// ends all of the user's sessions, so the user signs in again with the new password.
    /// </summary>
    public async Task ChangePasswordAsync(RequestContext me, string currentPassword, string newPassword, CancellationToken ct)
    {
        if (me.IsPlatformAdmin || !me.IsAuthenticated)
            throw new DomainException(ErrorKind.Forbidden, "Only company users change passwords here.");
        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
            throw new DomainException(ErrorKind.Validation, "Current and new password are required.");
        if (newPassword.Length < UserCreationRules.MinPasswordLength)
            throw new DomainException(ErrorKind.Validation,
                $"New password needs at least {UserCreationRules.MinPasswordLength} characters.");
        if (newPassword == currentPassword)
            throw new DomainException(ErrorKind.Validation, "The new password must be different from the current one.");

        var storedHash = await repo.GetPasswordHashByUserIdAsync(me.CompanyId, me.UserId, ct);
        if (storedHash is null || !hasher.Verify(currentPassword, storedHash))
        {
            log.LogWarning("Password change refused: wrong current password for user {UserId}", me.UserId);
            throw new DomainException(ErrorKind.Unauthorized, "Current password is wrong.");
        }

        await repo.ChangePasswordAsync(me.CompanyId, me.UserId, hasher.Hash(newPassword), ct);
        await uow.CommitAsync(ct);
        log.LogInformation("Password changed for user {UserId}; all their sessions ended", me.UserId);
    }

    /// <summary>Ends the session in the database; the token stops working immediately.</summary>
    public async Task LogoutAsync(Guid companyId, string token, CancellationToken ct)
    {
        await repo.LogoutAsync(companyId, token, ct);
        await uow.CommitAsync(ct);
        log.LogInformation("Signed out: session of company {CompanyId} ended", companyId);
    }

    /// <summary>Splits "{companyId}.{secret}" into its two parts. Returns false if the shape is wrong.</summary>
    public static bool TryParseToken(string? raw, out Guid companyId, out string secret)
    {
        companyId = Guid.Empty; secret = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var dot = raw.IndexOf('.');
        if (dot <= 0 || dot == raw.Length - 1) return false;
        if (!Guid.TryParse(raw.AsSpan(0, dot), out companyId) || companyId == Guid.Empty) return false;
        secret = raw[(dot + 1)..];
        return true;
    }
}
