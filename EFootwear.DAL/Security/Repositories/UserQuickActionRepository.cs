using EFootwear.BLL.Security.Abstractions;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Security.Repositories;

/// <summary>DAL for the quick-action toolbar of a user (sec.user_quick_actions).</summary>
public sealed class UserQuickActionRepository(DatabaseSession databaseSession) : IUserQuickActionRepository
{
    public async Task<IReadOnlyList<string>> GetActionCodesAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.UserQuickActions.AsNoTracking()
            .Where(quickAction => quickAction.CompanyId == companyId && quickAction.UserId == userId)
            .OrderBy(quickAction => quickAction.SortOrder)
            .Select(quickAction => quickAction.ActionCode)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceAsync(Guid companyId, Guid userId, IReadOnlyList<string> actionCodes, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        await databaseContext.UserQuickActions
            .Where(quickAction => quickAction.CompanyId == companyId && quickAction.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        databaseContext.UserQuickActions.AddRange(actionCodes.Select((actionCode, index) => new UserQuickAction
        {
            CompanyId = companyId, UserId = userId, ActionCode = actionCode, SortOrder = index,
        }));
    }
}
