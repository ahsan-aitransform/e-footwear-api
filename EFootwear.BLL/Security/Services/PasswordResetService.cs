using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// Forgot password, for people who cannot sign in (anonymous endpoints). Two steps:
///  1. <see cref="RequestAsync"/>: always answers the same, whether or not the email has an account, so nobody can find out which
///     emails exist. When there is an account, a single-use token is created and handed to <see cref="IPasswordResetTokenDelivery"/>.
///     No delivery channel is approved yet ([BUSINESS DECISION REQUIRED]), so the default delivery sends nothing; until then
///     administrators issue tokens by hand (UserAccountSecurityService).
///  2. <see cref="CompleteAsync"/>: the token ("{companyId}.{secret}") plus a new password. The token works once, expires, and the
///     database ends every session of the user.
/// </summary>
public sealed class PasswordResetService(
    IUserAuthenticationRepository authenticationRepository,
    IPasswordResetRepository passwordResetRepository,
    IPasswordResetTokenDelivery tokenDelivery,
    IPasswordHasher passwordHasher,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    ILogger<PasswordResetService> logger)
{
    public async Task RequestAsync(RequestPasswordResetRequest request, string? clientIpAddress, CancellationToken cancellationToken)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0) return;

        if (await authenticationRepository.FindCompanyIdByEmailAsync(email, cancellationToken) is not { } companyId)
        {
            logger.LogWarning("Password reset requested for an unknown email {Email}", LogMask.Email(email));
            return;
        }

        var issued = await passwordResetRepository.IssueTokenAsync(companyId, email, clientIpAddress, cancellationToken);
        if (issued is not null)
        {
            var lifetimeMinutes = await passwordResetRepository.GetTokenLifetimeMinutesAsync(companyId, cancellationToken);
            await tokenDelivery.DeliverAsync(new PasswordResetDelivery(companyId, issued.UserId, email, issued.DeliveryPhoneNumber,
                $"{companyId}{SessionTokenFormat.CompanySeparator}{issued.TokenSecret}",
                dateTimeProvider.UtcNow.AddMinutes(lifetimeMinutes)), cancellationToken);
        }

        // Commit also when nothing was issued: a throttled request is written to the login history.
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task CompleteAsync(CompletePasswordResetRequest request, CancellationToken cancellationToken)
    {
        if (!UserAuthenticationService.TryParseToken(request.ResetToken, out var companyId, out var tokenSecret))
            throw new DomainException(ErrorKind.Unauthorized, "The reset token is invalid, used or expired.");
        PasswordRules.EnsureValid(request.NewPassword);

        await passwordResetRepository.CompleteAsync(companyId, tokenSecret, passwordHasher.Hash(request.NewPassword), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("A password was reset with a token in company {CompanyId}; all sessions of the user ended", companyId);
    }
}
