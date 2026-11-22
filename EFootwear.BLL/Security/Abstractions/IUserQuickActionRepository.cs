namespace EFootwear.BLL.Security.Abstractions;

/// <summary>BLL contract for the quick-action toolbar of a user (sec.user_quick_actions).</summary>
public interface IUserQuickActionRepository
{
    Task<IReadOnlyList<string>> GetActionCodesAsync(Guid companyId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Makes the toolbar exactly these codes, in this order.</summary>
    Task ReplaceAsync(Guid companyId, Guid userId, IReadOnlyList<string> actionCodes, CancellationToken cancellationToken);
}
