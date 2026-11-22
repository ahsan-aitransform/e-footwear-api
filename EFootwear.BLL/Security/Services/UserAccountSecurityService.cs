using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// Block, unblock and password actions an administrator performs on ANOTHER user's account (docx 1.3, Part 6). The rules for who may
/// do this to whom are in <see cref="UserManagementRules"/>; the database functions (sec.set_user_status, sec.admin_reset_password)
/// end the user's sessions and write the login history. Passwords are hashed here; the database never sees a plain password.
/// </summary>
public sealed class UserAccountSecurityService(
    UserManagementService userManagementService,
    IUserRepository userRepository,
    IPasswordResetRepository passwordResetRepository,
    IActivityLogWriter activityLogWriter,
    IPasswordHasher passwordHasher,
    IDateTimeProvider dateTimeProvider,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<UserAccountSecurityService> logger)
{
    public async Task BlockAsync(Guid userId, CancellationToken cancellationToken)
    {
        await userManagementService.FindManageableUserAsync(userId, UserManagementAction.ChangeStatus, cancellationToken);
        await userRepository.SetStatusAsync(requestContext.CompanyId, userId, UserStatusCodes.Disabled, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("User {UserId} blocked by {ActorUserId}; their sessions ended", userId, requestContext.UserId);
    }

    public async Task UnblockAsync(Guid userId, CancellationToken cancellationToken)
    {
        await userManagementService.FindManageableUserAsync(userId, UserManagementAction.ChangeStatus, cancellationToken);
        await userRepository.SetStatusAsync(requestContext.CompanyId, userId, UserStatusCodes.Active, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("User {UserId} unblocked by {ActorUserId}", userId, requestContext.UserId);
    }

    /// <summary>Sets a temporary password. The user must change it at the next sign-in and all their sessions end.</summary>
    public async Task ResetPasswordAsync(Guid userId, ResetUserPasswordRequest request, CancellationToken cancellationToken)
    {
        PasswordRules.EnsureValid(request.TemporaryPassword, subjectName: "Temporary password");
        await userManagementService.FindManageableUserAsync(userId, UserManagementAction.ResetPassword, cancellationToken);

        await userRepository.AdminResetPasswordAsync(requestContext.CompanyId, userId,
                                                     passwordHasher.Hash(request.TemporaryPassword), cancellationToken);
        await activityLogWriter.WriteAsync(new ActivityLogEntryRecord(requestContext.CompanyId,
            SecurityActivityActions.UserPasswordResetByAdministrator, SecurityActivityActions.UserEntityType, userId.ToString(),
            null, null, "Temporary password set; sessions ended"), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Password of user {UserId} reset by {ActorUserId}", userId, requestContext.UserId);
    }

    /// <summary>
    /// Issues a single-use reset token and returns it once, for the administrator to hand to the user. This is the only way a token
    /// reaches a person until a delivery channel is approved ([BUSINESS DECISION REQUIRED]). Counts against the hourly limit of the
    /// company's auth policy.
    /// </summary>
    public async Task<IssuedPasswordResetTokenResponse> IssuePasswordResetTokenAsync(Guid userId, string? clientIpAddress,
                                                                                     CancellationToken cancellationToken)
    {
        var user = await userManagementService.FindManageableUserAsync(userId, UserManagementAction.ResetPassword, cancellationToken);

        var issued = await passwordResetRepository.IssueTokenAsync(requestContext.CompanyId, user.Email, clientIpAddress, cancellationToken)
            ?? throw new DomainException(ErrorKind.Conflict,
                "No reset token can be issued: the account is disabled or too many tokens were requested in the last hour.",
                ErrorCodes.TooManyRequests);
        var lifetimeMinutes = await passwordResetRepository.GetTokenLifetimeMinutesAsync(requestContext.CompanyId, cancellationToken);

        await activityLogWriter.WriteAsync(new ActivityLogEntryRecord(requestContext.CompanyId,
            SecurityActivityActions.PasswordResetTokenIssued, SecurityActivityActions.UserEntityType, userId.ToString(),
            null, null, "Reset token issued by an administrator"), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Password reset token issued for user {UserId} by {ActorUserId}", userId, requestContext.UserId);

        return new IssuedPasswordResetTokenResponse(
            $"{requestContext.CompanyId}{SessionTokenFormat.CompanySeparator}{issued.TokenSecret}",
            dateTimeProvider.UtcNow.AddMinutes(lifetimeMinutes));
    }
}
