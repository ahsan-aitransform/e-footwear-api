using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Accounting.Abstractions;

/// <summary>
/// Data access for the chart of accounts: second-level groups (docx 2.2, 2.3) and accounts (docx 2.1). Master data only: nothing
/// here posts to the journal. Implemented in EFootwear.DAL/Accounting/Repositories/ChartOfAccountsRepository.cs.
/// Updates take the row version the client saw; a stale value makes the save fail with a concurrency conflict (409).
/// </summary>
public interface IChartOfAccountsRepository
{
    Task<IReadOnlyList<AccountGroupResponse>> ListGroupsAsync(string? mainTypeCode, CancellationToken cancellationToken);
    Task<AccountGroupResponse?> FindGroupAsync(string groupCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListGroupCodesOfMainTypeAsync(string mainTypeCode, CancellationToken cancellationToken);
    Task<bool> GroupTitleExistsAsync(string mainTypeCode, string title, string? exceptGroupCode, CancellationToken cancellationToken);
    Task AddGroupAsync(string mainTypeCode, string groupCode, string title, Guid createdBy, CancellationToken cancellationToken);
    Task UpdateGroupTitleAsync(string groupCode, string title, long rowVersion, CancellationToken cancellationToken);
    Task DeleteGroupAsync(string groupCode, CancellationToken cancellationToken);

    Task<PagedResult<AccountResponse>> ListAccountsAsync(string? groupCode, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<AccountResponse?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken);
    Task<string> GetNextAccountCodeAsync(string groupCode, CancellationToken cancellationToken);
    Task<bool> AccountTitleExistsInGroupAsync(string groupCode, string title, Guid? exceptAccountId, CancellationToken cancellationToken);
    Task<Guid> AddAccountAsync(string code, string mainTypeCode, string groupCode, string title, Guid createdBy, CancellationToken cancellationToken);
    Task UpdateAccountAsync(Guid accountId, string title, bool isActive, long rowVersion, CancellationToken cancellationToken);
    Task DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken);
}
