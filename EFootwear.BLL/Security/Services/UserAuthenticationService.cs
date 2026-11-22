using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// BLL (Business Logic Layer) for signing in and out. This class decides WHAT happens; it never touches
/// SQL or HTTP directly. It talks to the database only through <see cref="IUserAuthenticationRepository"/>,
/// so it can be unit-tested with a fake repository.
/// </summary>
public sealed class UserAuthenticationService(
    IUserAuthenticationRepository authenticationRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ILogger<UserAuthenticationService> logger)
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
    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? clientIpAddress, string? userAgent,
                                                CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            throw new DomainException(ErrorKind.Validation, "Email and password are required.");

        // The email is unique across all companies, so it alone tells us which company to look in.
        var email = request.Email.Trim().ToLowerInvariant();
        if (await authenticationRepository.FindCompanyIdByEmailAsync(email, cancellationToken) is not { } companyId)
        {
            // Unknown email: same answer, and same time, as a wrong password.
            passwordHasher.BurnTime(request.Password);
            logger.LogWarning("Sign-in refused: no account for {Email} (from {ClientIpAddress})",
                LogMask.Email(email), clientIpAddress);
            throw new DomainException(ErrorKind.Unauthorized, "Invalid email or password.");
        }

        // Before login nobody is signed in, so we tell the database which company to look in.
        authenticationRepository.UseCompany(companyId);

        var storedPasswordHash = await authenticationRepository.GetPasswordHashAsync(email, cancellationToken);
        bool isPasswordCorrect;
        if (storedPasswordHash is null)
        {
            // Unknown user: do a fake password check anyway so the response time does not reveal
            // whether the username exists.
            passwordHasher.BurnTime(request.Password);
            isPasswordCorrect = false;
        }
        else isPasswordCorrect = passwordHasher.Verify(request.Password, storedPasswordHash);

        var outcome = await authenticationRepository.LoginAsync(companyId, email, isPasswordCorrect,
                                                                clientIpAddress, userAgent, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        switch (outcome.Outcome)
        {
            case LoginOutcomeCodes.Succeeded:
                logger.LogInformation("Sign-in succeeded: user {UserId} of company {CompanyId} (from {ClientIpAddress})",
                    outcome.UserId, companyId, clientIpAddress);
                return new LoginResponse($"{companyId}{SessionTokenFormat.CompanySeparator}{outcome.SessionToken}",
                                         outcome.MustChangePassword);
            case LoginOutcomeCodes.Locked:
                logger.LogWarning("Sign-in refused: account {Email} of company {CompanyId} is locked until {LockedUntil:u} (from {ClientIpAddress})",
                    LogMask.Email(email), companyId, outcome.LockedUntil, clientIpAddress);
                throw new DomainException(ErrorKind.Locked, $"Account is locked until {outcome.LockedUntil:u}.");
            default:
                // Wrong password, unknown user and disabled user all give the SAME message on purpose.
                logger.LogWarning("Sign-in failed: wrong password, or blocked account, for {Email} of company {CompanyId} (from {ClientIpAddress})",
                    LogMask.Email(email), companyId, clientIpAddress);
                throw new DomainException(ErrorKind.Unauthorized, "Invalid email or password.");
        }
    }

    /// <summary>
    /// Changes the signed-in user's own password. The current password must be right. The database then
    /// ends all of the user's sessions, so the user signs in again with the new password.
    /// Every signed-in company user may do this and it needs no permission, because a user who still has a
    /// temporary password must be able to change it.
    /// </summary>
    public async Task ChangePasswordAsync(RequestContext requestContext, string currentPassword, string newPassword,
                                          CancellationToken cancellationToken)
    {
        if (requestContext.IsPlatformAdministrator || !requestContext.IsAuthenticated)
            throw new DomainException(ErrorKind.Forbidden, "Only company users change passwords here.");
        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
            throw new DomainException(ErrorKind.Validation, "Current and new password are required.");
        PasswordRules.EnsureValid(newPassword, currentPassword);

        var storedPasswordHash = await authenticationRepository.GetPasswordHashByUserIdAsync(
            requestContext.CompanyId, requestContext.UserId, cancellationToken);
        if (storedPasswordHash is null || !passwordHasher.Verify(currentPassword, storedPasswordHash))
        {
            logger.LogWarning("Password change refused: wrong current password for user {UserId}", requestContext.UserId);
            throw new DomainException(ErrorKind.Unauthorized, "Current password is wrong.");
        }

        await authenticationRepository.ChangePasswordAsync(requestContext.CompanyId, requestContext.UserId,
                                                           passwordHasher.Hash(newPassword), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Password changed for user {UserId}; all their sessions ended", requestContext.UserId);
    }

    /// <summary>Ends the session in the database; the token stops working immediately.</summary>
    public async Task LogoutAsync(Guid companyId, string token, CancellationToken cancellationToken)
    {
        await authenticationRepository.LogoutAsync(companyId, token, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Signed out: session of company {CompanyId} ended", companyId);
    }

    /// <summary>Splits "{companyId}.{secret}" into its two parts. Returns false if the shape is wrong.</summary>
    public static bool TryParseToken(string? rawToken, out Guid companyId, out string secret)
    {
        companyId = Guid.Empty;
        secret = "";
        if (string.IsNullOrWhiteSpace(rawToken)) return false;
        var separatorIndex = rawToken.IndexOf(SessionTokenFormat.CompanySeparator);
        if (separatorIndex <= 0 || separatorIndex == rawToken.Length - 1) return false;
        if (!Guid.TryParse(rawToken.AsSpan(0, separatorIndex), out companyId) || companyId == Guid.Empty) return false;
        secret = rawToken[(separatorIndex + 1)..];
        return true;
    }
}
