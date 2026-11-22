using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.MasterData.Services;

/// <summary>
/// Use cases shared by every simple master list (docx 2.4 to 2.11, 2.17): list with search, create, edit, delete. The name is
/// required, trimmed and unique without regard to case; an item that anything still uses cannot be deleted (the message says
/// where it is used); deleting is a soft delete so history stays; cities warn about near-duplicate spellings.
/// The caller (a thin controller per list) passes the <see cref="MasterListDefinition"/> and has already checked the permission.
/// </summary>
public sealed class MasterListService(
    IMasterListRepository masterListRepository,
    IRowUsageRepository rowUsageRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<MasterListService> logger)
{
    public Task<PagedResult<MasterListItemResponse>> ListAsync(MasterListDefinition definition, PageRequest pageRequest, bool includeInactive,
                                                               CancellationToken cancellationToken) =>
        masterListRepository.ListAsync(definition, pageRequest.Normalized(), includeInactive, cancellationToken);

    public Task<IReadOnlyList<MasterListOptionResponse>> ListOptionsAsync(MasterListDefinition definition, CancellationToken cancellationToken) =>
        masterListRepository.ListActiveOptionsAsync(definition, cancellationToken);

    public async Task<MasterListItemResponse> GetAsync(MasterListDefinition definition, Guid itemId, CancellationToken cancellationToken) =>
        await masterListRepository.FindAsync(definition, itemId, cancellationToken)
        ?? throw NotFound(definition);

    public async Task<MasterListItemSavedResponse> CreateAsync(MasterListDefinition definition, CreateMasterListItemRequest request,
                                                               CancellationToken cancellationToken)
    {
        var normalized = request with
        {
            Name = MasterListRules.NormalizeText(request.Name),
            NameUrdu = MasterListRules.NormalizeOptionalText(request.NameUrdu),
            Code = MasterListRules.NormalizeOptionalText(request.Code),
        };
        MasterListRules.EnsureValid(normalized.Name, normalized.NameUrdu, normalized.Code, normalized.SortOrder, definition.DisplayName);
        await EnsureNameIsFreeAsync(definition, normalized.Name, null, cancellationToken);
        var warnings = await FindNearDuplicatesAsync(definition, normalized.Name, null, cancellationToken);

        var itemId = await masterListRepository.AddAsync(definition, normalized, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("{List} item {ItemId} created in company {CompanyId} by user {UserId}", definition.Key, itemId, currentUser.CompanyId, currentUser.UserId);
        return new MasterListItemSavedResponse(itemId, warnings);
    }

    public async Task<MasterListItemSavedResponse> UpdateAsync(MasterListDefinition definition, Guid itemId, UpdateMasterListItemRequest request,
                                                               CancellationToken cancellationToken)
    {
        _ = await GetAsync(definition, itemId, cancellationToken);
        var normalized = request with
        {
            Name = MasterListRules.NormalizeText(request.Name),
            NameUrdu = MasterListRules.NormalizeOptionalText(request.NameUrdu),
            Code = MasterListRules.NormalizeOptionalText(request.Code),
        };
        MasterListRules.EnsureValid(normalized.Name, normalized.NameUrdu, normalized.Code, normalized.SortOrder, definition.DisplayName);
        await EnsureNameIsFreeAsync(definition, normalized.Name, itemId, cancellationToken);
        var warnings = await FindNearDuplicatesAsync(definition, normalized.Name, itemId, cancellationToken);

        await masterListRepository.UpdateAsync(definition, itemId, normalized, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return new MasterListItemSavedResponse(itemId, warnings);
    }

    public async Task DeleteAsync(MasterListDefinition definition, Guid itemId, CancellationToken cancellationToken)
    {
        var item = await GetAsync(definition, itemId, cancellationToken);
        var usages = await rowUsageRepository.FindUsagesAsync(definition.QualifiedTableName, itemId, cancellationToken);
        RowUsageMessage.ThrowWhenUsed($"{definition.DisplayName} \"{item.Name}\"", usages);

        await masterListRepository.SoftDeleteAsync(definition, itemId, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("{List} item {ItemId} deleted in company {CompanyId} by user {UserId}", definition.Key, itemId, currentUser.CompanyId, currentUser.UserId);
    }

    private async Task EnsureNameIsFreeAsync(MasterListDefinition definition, string name, Guid? exceptItemId, CancellationToken cancellationToken)
    {
        if (await masterListRepository.NameExistsAsync(definition, name, exceptItemId, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"{definition.DisplayName} \"{name}\" already exists.");
    }

    private async Task<IReadOnlyList<string>> FindNearDuplicatesAsync(MasterListDefinition definition, string name, Guid? exceptItemId,
                                                                      CancellationToken cancellationToken)
    {
        if (!definition.WarnAboutNearDuplicateNames) return [];
        var existingNames = await masterListRepository.ListNamesAsync(definition, cancellationToken);
        var current = exceptItemId is null ? null : (await masterListRepository.FindAsync(definition, exceptItemId.Value, cancellationToken))?.Name;
        return NearDuplicateNameDetector.FindNearDuplicates(name, existingNames.Where(existing => !string.Equals(existing, current, StringComparison.OrdinalIgnoreCase)));
    }

    private static DomainException NotFound(MasterListDefinition definition) =>
        new(ErrorKind.NotFound, $"{definition.DisplayName} was not found.");
}
