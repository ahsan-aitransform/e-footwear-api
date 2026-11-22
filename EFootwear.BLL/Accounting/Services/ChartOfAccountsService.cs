using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Common;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Accounting.Services;

/// <summary>
/// Use cases of the chart of accounts master data (docx 2.1 GL-Accounts, 2.2 New Second Level, 2.3 View Second Level).
/// Rules: a group code is the digit of its main type plus a two-digit serial; an account code is the group code plus a
/// five-digit serial; titles are unique (within the main type for groups, within the group for accounts); a group that has
/// accounts and an account that anything uses cannot be deleted; system accounts cannot be deleted or deactivated because the
/// posting functions of the database look them up by role.
/// </summary>
public sealed class ChartOfAccountsService(
    IChartOfAccountsRepository chartOfAccountsRepository,
    IRowUsageRepository rowUsageRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<ChartOfAccountsService> logger)
{
    private const string AccountsTableName = "acct.accounts";

    public IReadOnlyList<AccountMainTypeResponse> ListMainTypes() =>
        AccountMainTypes.All
            .Select(mainType => new AccountMainTypeResponse(mainType.Code, mainType.DigitPrefix.ToString(), mainType.DisplayName))
            .ToList();

    // ----------------------------------------------------------------- groups

    public async Task<IReadOnlyList<AccountGroupResponse>> ListGroupsAsync(string? mainTypeCode, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(mainTypeCode)) RequireMainType(mainTypeCode);
        return await chartOfAccountsRepository.ListGroupsAsync(string.IsNullOrWhiteSpace(mainTypeCode) ? null : mainTypeCode, cancellationToken);
    }

    public async Task<NextAccountGroupCodeResponse> GetNextGroupCodeAsync(string mainTypeCode, CancellationToken cancellationToken)
    {
        var mainType = RequireMainType(mainTypeCode);
        var existingCodes = await chartOfAccountsRepository.ListGroupCodesOfMainTypeAsync(mainType.Code, cancellationToken);
        return new NextAccountGroupCodeResponse(ChartOfAccountsRules.NextGroupCode(mainType, existingCodes));
    }

    public async Task<NextAccountGroupCodeResponse> CreateGroupAsync(CreateAccountGroupRequest request, CancellationToken cancellationToken)
    {
        var mainType = RequireMainType(request.MainTypeCode);
        var title = ChartOfAccountsRules.NormalizeTitle(request.Name, "Account group");
        if (await chartOfAccountsRepository.GroupTitleExistsAsync(mainType.Code, title, null, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"{mainType.DisplayName} already has a group titled \"{title}\".");

        var existingCodes = await chartOfAccountsRepository.ListGroupCodesOfMainTypeAsync(mainType.Code, cancellationToken);
        var groupCode = ChartOfAccountsRules.NextGroupCode(mainType, existingCodes);
        await chartOfAccountsRepository.AddGroupAsync(mainType.Code, groupCode, title, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Account group {GroupCode} created in company {CompanyId} by user {UserId}", groupCode, currentUser.CompanyId, currentUser.UserId);
        return new NextAccountGroupCodeResponse(groupCode);
    }

    public async Task UpdateGroupAsync(string groupCode, UpdateAccountGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await RequireGroupAsync(groupCode, cancellationToken);
        var title = ChartOfAccountsRules.NormalizeTitle(request.Name, "Account group");
        if (await chartOfAccountsRepository.GroupTitleExistsAsync(group.MainTypeCode, title, group.GroupCode, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"Another group of the same main type is already titled \"{title}\".");

        await chartOfAccountsRepository.UpdateGroupTitleAsync(group.GroupCode, title, request.RowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteGroupAsync(string groupCode, CancellationToken cancellationToken)
    {
        var group = await RequireGroupAsync(groupCode, cancellationToken);
        if (group.AccountCount > 0)
            throw new DomainException(ErrorKind.Conflict,
                $"Group {group.GroupCode} {group.Name} cannot be deleted because {group.AccountCount} account(s) use it.", ErrorCodes.RecordInUse);

        await chartOfAccountsRepository.DeleteGroupAsync(group.GroupCode, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Account group {GroupCode} deleted in company {CompanyId} by user {UserId}", group.GroupCode, currentUser.CompanyId, currentUser.UserId);
    }

    // --------------------------------------------------------------- accounts

    public async Task<PagedResult<AccountResponse>> ListAccountsAsync(string? groupCode, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(groupCode) && !ChartOfAccountsRules.IsValidGroupCode(groupCode))
            throw new DomainException(ErrorKind.Validation, "The group code must be three digits, for example 102.");
        return await chartOfAccountsRepository.ListAccountsAsync(string.IsNullOrWhiteSpace(groupCode) ? null : groupCode, pageRequest.Normalized(), cancellationToken);
    }

    public async Task<NextAccountCodeResponse> GetNextAccountCodeAsync(string groupCode, CancellationToken cancellationToken)
    {
        var group = await RequireGroupAsync(groupCode, cancellationToken);
        return new NextAccountCodeResponse(await chartOfAccountsRepository.GetNextAccountCodeAsync(group.GroupCode, cancellationToken));
    }

    public async Task<CreatedIdResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var group = await RequireGroupAsync(request.GroupCode, cancellationToken);
        var title = ChartOfAccountsRules.NormalizeTitle(request.Name, "Account");
        if (await chartOfAccountsRepository.AccountTitleExistsInGroupAsync(group.GroupCode, title, null, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"Group {group.GroupCode} {group.Name} already has an account titled \"{title}\".");

        var accountCode = await chartOfAccountsRepository.GetNextAccountCodeAsync(group.GroupCode, cancellationToken);
        var accountId = await chartOfAccountsRepository.AddAccountAsync(accountCode, group.MainTypeCode, group.GroupCode, title, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Account {AccountCode} created in company {CompanyId} by user {UserId}", accountCode, currentUser.CompanyId, currentUser.UserId);
        return new CreatedIdResponse(accountId);
    }

    public async Task UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(accountId, cancellationToken);
        var title = ChartOfAccountsRules.NormalizeTitle(request.Name, "Account");
        if (account.SystemRole is not null && !request.IsActive)
            throw new DomainException(ErrorKind.Conflict, $"Account {account.Code} is a system account ({account.SystemRole}) and cannot be deactivated.");
        if (await chartOfAccountsRepository.AccountTitleExistsInGroupAsync(account.GroupCode, title, account.Id, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"Group {account.GroupCode} already has another account titled \"{title}\".");

        await chartOfAccountsRepository.UpdateAccountAsync(account.Id, title, request.IsActive, request.RowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(accountId, cancellationToken);
        if (account.SystemRole is not null)
            throw new DomainException(ErrorKind.Conflict,
                $"Account {account.Code} is a system account ({account.SystemRole}) and cannot be deleted.", ErrorCodes.RecordInUse);

        var usages = await rowUsageRepository.FindUsagesAsync(AccountsTableName, account.Id, cancellationToken);
        RowUsageMessage.ThrowWhenUsed($"Account {account.Code} {account.Name}", usages);

        await chartOfAccountsRepository.DeleteAccountAsync(account.Id, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Account {AccountCode} deleted in company {CompanyId} by user {UserId}", account.Code, currentUser.CompanyId, currentUser.UserId);
    }

    // ---------------------------------------------------------------- helpers

    private static AccountMainType RequireMainType(string? mainTypeCode) =>
        AccountMainTypes.FindByCode(mainTypeCode)
        ?? throw new DomainException(ErrorKind.Validation,
            $"Unknown main type \"{mainTypeCode}\". Use one of: {string.Join(", ", AccountMainTypes.All.Select(mainType => mainType.Code))}.");

    private async Task<AccountGroupResponse> RequireGroupAsync(string groupCode, CancellationToken cancellationToken)
    {
        if (!ChartOfAccountsRules.IsValidGroupCode(groupCode))
            throw new DomainException(ErrorKind.Validation, "The group code must be three digits, for example 102.");
        return await chartOfAccountsRepository.FindGroupAsync(groupCode, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, $"Account group {groupCode} was not found.");
    }

    private async Task<AccountResponse> RequireAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        await chartOfAccountsRepository.FindAccountAsync(accountId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The account was not found.");
}
