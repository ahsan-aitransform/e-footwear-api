using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.Domain.Security;

namespace EFootwear.BLL.Security.Services;

/// <summary>
/// The quick-action toolbar of the original system (docx 1.1 "Standard Menu"): which buttons a user sees. Everybody reads their own
/// toolbar; an administrator edits it for the users they may manage. The buttons only choose what is shown. What a user may open is
/// always decided by their permissions.
/// </summary>
public sealed class UserQuickActionService(
    UserManagementService userManagementService,
    IUserQuickActionRepository quickActionRepository,
    IActivityLogWriter activityLogWriter,
    RequestContext requestContext,
    IUnitOfWork unitOfWork)
{
    public async Task<UserQuickActionsResponse> GetOwnAsync(CancellationToken cancellationToken) =>
        new(await quickActionRepository.GetActionCodesAsync(requestContext.CompanyId, requestContext.UserId, cancellationToken));

    public async Task<UserQuickActionsResponse> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await userManagementService.FindManageableUserAsync(userId, UserManagementAction.View, cancellationToken);
        return new(await quickActionRepository.GetActionCodesAsync(requestContext.CompanyId, userId, cancellationToken));
    }

    public async Task ReplaceForUserAsync(Guid userId, ReplaceUserQuickActionsRequest request, CancellationToken cancellationToken)
    {
        await userManagementService.FindManageableUserAsync(userId, UserManagementAction.EditProfile, cancellationToken);
        var actionCodes = (request.ActionCodes ?? []).Distinct().ToList();
        QuickActionCodes.EnsureAllKnown(actionCodes);

        var current = await quickActionRepository.GetActionCodesAsync(requestContext.CompanyId, userId, cancellationToken);
        if (current.SequenceEqual(actionCodes)) return;

        await quickActionRepository.ReplaceAsync(requestContext.CompanyId, userId, actionCodes, cancellationToken);
        await activityLogWriter.WriteAsync(new ActivityLogEntryRecord(requestContext.CompanyId, SecurityActivityActions.QuickActionsChanged,
            SecurityActivityActions.UserQuickActionsEntityType, userId.ToString(), new { actionCodes = current }, new { actionCodes }, null),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
