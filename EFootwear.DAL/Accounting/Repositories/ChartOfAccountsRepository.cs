using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Accounting.Repositories;

/// <summary>Data access for account groups (acct.account_groups) and accounts (acct.accounts). Master data only: nothing here posts to the journal.</summary>
public sealed class ChartOfAccountsRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IChartOfAccountsRepository
{
    private static readonly SortableFields<AccountRow> SortableAccountFields = new SortableFields<AccountRow>()
        .Allow("code", row => row.Account.Code)
        .Allow("group", row => row.Account.GroupCode)
        .Allow("title", row => row.Account.Name);

    // ----------------------------------------------------------------- groups

    public async Task<IReadOnlyList<AccountGroupResponse>> ListGroupsAsync(string? mainTypeCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var groups = databaseContext.AccountGroups.AsNoTracking().Where(group => group.CompanyId == companyId);
        if (mainTypeCode is not null) groups = groups.Where(group => group.MainTypeCode == mainTypeCode);

        return await groups.OrderBy(group => group.GroupCode)
            .Select(group => new AccountGroupResponse(
                group.GroupCode, group.MainTypeCode, group.Name,
                databaseContext.GeneralLedgerAccounts.Count(account => account.CompanyId == group.CompanyId && account.GroupCode == group.GroupCode),
                group.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountGroupResponse?> FindGroupAsync(string groupCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.AccountGroups.AsNoTracking()
            .Where(group => group.CompanyId == companyId && group.GroupCode == groupCode)
            .Select(group => new AccountGroupResponse(
                group.GroupCode, group.MainTypeCode, group.Name,
                databaseContext.GeneralLedgerAccounts.Count(account => account.CompanyId == group.CompanyId && account.GroupCode == group.GroupCode),
                group.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListGroupCodesOfMainTypeAsync(string mainTypeCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.AccountGroups.AsNoTracking()
            .Where(group => group.CompanyId == companyId && group.MainTypeCode == mainTypeCode)
            .Select(group => group.GroupCode).ToListAsync(cancellationToken);
    }

    public async Task<bool> GroupTitleExistsAsync(string mainTypeCode, string title, string? exceptGroupCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var lowerTitle = title.ToLowerInvariant();
        return await databaseContext.AccountGroups.AsNoTracking().AnyAsync(group =>
            group.CompanyId == companyId && group.MainTypeCode == mainTypeCode && group.Name.ToLower() == lowerTitle
            && (exceptGroupCode == null || group.GroupCode != exceptGroupCode), cancellationToken);
    }

    public async Task AddGroupAsync(string mainTypeCode, string groupCode, string title, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.AccountGroups.Add(new AccountGroup
        {
            CompanyId = currentUser.CompanyId,
            GroupCode = groupCode,
            MainTypeCode = mainTypeCode,
            Name = title,
            CreatedBy = createdBy,
        });
    }

    public async Task UpdateGroupTitleAsync(string groupCode, string title, long rowVersion, CancellationToken cancellationToken)
    {
        var group = await RequireTrackedGroupAsync(groupCode, cancellationToken);
        group.Name = title;
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.Entry(group).Property(entity => entity.RowVersion).OriginalValue = rowVersion;
    }

    public async Task DeleteGroupAsync(string groupCode, CancellationToken cancellationToken)
    {
        var group = await RequireTrackedGroupAsync(groupCode, cancellationToken);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.AccountGroups.Remove(group);
    }

    // --------------------------------------------------------------- accounts

    public async Task<PagedResult<AccountResponse>> ListAccountsAsync(string? groupCode, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var rows = await AccountRowsAsync(cancellationToken);
        if (groupCode is not null) rows = rows.Where(row => row.Account.GroupCode == groupCode);
        if (pageRequest.SearchText is { } searchText)
        {
            var pattern = LikePattern.Contains(searchText);
            rows = rows.Where(row => EF.Functions.ILike(row.Account.Name, pattern, LikePattern.EscapeCharacter)
                                     || EF.Functions.ILike(row.Account.Code, pattern, LikePattern.EscapeCharacter));
        }

        var totalItemCount = await rows.LongCountAsync(cancellationToken);
        var page = await rows.ApplySortingAndPaging(pageRequest, SortableAccountFields, row => row.Account.Code).ToListAsync(cancellationToken);
        return new PagedResult<AccountResponse>(page.Select(ToResponse).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalItemCount);
    }

    public async Task<AccountResponse?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var row = await (await AccountRowsAsync(cancellationToken)).Where(candidate => candidate.Account.Id == accountId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToResponse(row);
    }

    public async Task<string> GetNextAccountCodeAsync(string groupCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Database
            .SqlQuery<string>($"select acct.next_account_code({companyId}, {groupCode}) as \"Value\"")
            .SingleAsync(cancellationToken);
    }

    public async Task<bool> AccountTitleExistsInGroupAsync(string groupCode, string title, Guid? exceptAccountId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var lowerTitle = title.ToLowerInvariant();
        return await databaseContext.GeneralLedgerAccounts.AsNoTracking().AnyAsync(account =>
            account.CompanyId == companyId && account.GroupCode == groupCode && account.Name.ToLower() == lowerTitle
            && (exceptAccountId == null || account.Id != exceptAccountId), cancellationToken);
    }

    public async Task<Guid> AddAccountAsync(string code, string mainTypeCode, string groupCode, string title, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var account = new GeneralLedgerAccount
        {
            CompanyId = currentUser.CompanyId,
            Code = code,
            MainTypeCode = mainTypeCode,
            GroupCode = groupCode,
            Name = title,
            StoreId = AccountingScope.HeadOfficeStoreId,
            CreatedBy = createdBy,
        };
        databaseContext.GeneralLedgerAccounts.Add(account);
        await databaseContext.SaveChangesAsync(cancellationToken);
        return account.Id;
    }

    public async Task UpdateAccountAsync(Guid accountId, string title, bool isActive, long rowVersion, CancellationToken cancellationToken)
    {
        var account = await RequireTrackedAccountAsync(accountId, cancellationToken);
        account.Name = title;
        account.IsActive = isActive;
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.Entry(account).Property(entity => entity.RowVersion).OriginalValue = rowVersion;
    }

    public async Task DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await RequireTrackedAccountAsync(accountId, cancellationToken);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.GeneralLedgerAccounts.Remove(account);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<IQueryable<AccountRow>> AccountRowsAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return from account in databaseContext.GeneralLedgerAccounts.AsNoTracking()
               where account.CompanyId == companyId
               join accountGroup in databaseContext.AccountGroups.AsNoTracking()
                   on new { account.CompanyId, account.GroupCode } equals new { accountGroup.CompanyId, accountGroup.GroupCode } into groupMatches
               from accountGroup in groupMatches.DefaultIfEmpty()
               select new AccountRow { Account = account, GroupName = accountGroup == null ? "" : accountGroup.Name };
    }

    private async Task<AccountGroup> RequireTrackedGroupAsync(string groupCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.AccountGroups.SingleOrDefaultAsync(group => group.CompanyId == companyId && group.GroupCode == groupCode, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, $"Account group {groupCode} was not found.");
    }

    private async Task<GeneralLedgerAccount> RequireTrackedAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.GeneralLedgerAccounts.SingleOrDefaultAsync(account => account.CompanyId == companyId && account.Id == accountId, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, "The account was not found.");
    }

    private static AccountResponse ToResponse(AccountRow row) => new(
        row.Account.Id, row.Account.Code, row.Account.GroupCode, row.GroupName, row.Account.MainTypeCode, row.Account.Name,
        row.Account.StoreId, row.Account.SystemRole, row.Account.IsPostable, row.Account.IsPartyControl, row.Account.IsActive, row.Account.RowVersion);

    private sealed class AccountRow
    {
        public GeneralLedgerAccount Account { get; set; } = null!;
        public string GroupName { get; set; } = "";
    }
}
