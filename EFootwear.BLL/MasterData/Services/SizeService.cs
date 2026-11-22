using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.BLL.MasterData.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.MasterData;

namespace EFootwear.BLL.MasterData.Services;

/// <summary>
/// Use cases for the sizes of a size group (part of the configurable attributes, Part 7: a size run is data, not code).
/// The size code is required and unique inside its group; a size that products or stock use cannot be deleted.
/// </summary>
public sealed class SizeService(
    ISizeRepository sizeRepository,
    IMasterListRepository masterListRepository,
    IRowUsageRepository rowUsageRepository,
    IUnitOfWork unitOfWork)
{
    public const int SizeCodeMaximumLength = 20;
    private const string SizesTableName = "master.sizes";

    public async Task<IReadOnlyList<SizeResponse>> ListAsync(Guid sizeGroupId, CancellationToken cancellationToken)
    {
        await EnsureSizeGroupExistsAsync(sizeGroupId, cancellationToken);
        return await sizeRepository.ListBySizeGroupAsync(sizeGroupId, cancellationToken);
    }

    public async Task<CreatedIdResponse> CreateAsync(Guid sizeGroupId, SaveSizeRequest request, CancellationToken cancellationToken)
    {
        await EnsureSizeGroupExistsAsync(sizeGroupId, cancellationToken);
        var sizeCode = NormalizeSizeCode(request);
        if (await sizeRepository.CodeExistsAsync(sizeGroupId, sizeCode, null, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"This size group already has the size \"{sizeCode}\".");

        var sizeId = await sizeRepository.AddAsync(sizeGroupId, sizeCode, request.SortOrder, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return new CreatedIdResponse(sizeId);
    }

    public async Task UpdateAsync(Guid sizeGroupId, Guid sizeId, SaveSizeRequest request, CancellationToken cancellationToken)
    {
        _ = await RequireSizeAsync(sizeGroupId, sizeId, cancellationToken);
        var sizeCode = NormalizeSizeCode(request);
        if (await sizeRepository.CodeExistsAsync(sizeGroupId, sizeCode, sizeId, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, $"This size group already has the size \"{sizeCode}\".");

        await sizeRepository.UpdateAsync(sizeGroupId, sizeId, sizeCode, request.SortOrder, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid sizeGroupId, Guid sizeId, CancellationToken cancellationToken)
    {
        var size = await RequireSizeAsync(sizeGroupId, sizeId, cancellationToken);
        var usages = await rowUsageRepository.FindUsagesAsync(SizesTableName, sizeId, cancellationToken);
        RowUsageMessage.ThrowWhenUsed($"Size \"{size.SizeCode}\"", usages);

        await sizeRepository.DeleteAsync(sizeGroupId, sizeId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    private static string NormalizeSizeCode(SaveSizeRequest request)
    {
        var sizeCode = MasterListRules.NormalizeText(request.SizeCode);
        if (sizeCode.Length == 0 || sizeCode.Length > SizeCodeMaximumLength)
            throw new DomainException(ErrorKind.Validation, $"The size is required (up to {SizeCodeMaximumLength} characters).");
        if (request.SortOrder is < 0 or > MasterListRules.SortOrderMaximum)
            throw new DomainException(ErrorKind.Validation, $"The sort order must be between 0 and {MasterListRules.SortOrderMaximum}.");
        return sizeCode;
    }

    private async Task EnsureSizeGroupExistsAsync(Guid sizeGroupId, CancellationToken cancellationToken)
    {
        if (await masterListRepository.FindAsync(MasterListDefinitions.SizeGroup, sizeGroupId, cancellationToken) is null)
            throw new DomainException(ErrorKind.NotFound, "The size group was not found.");
    }

    private async Task<SizeResponse> RequireSizeAsync(Guid sizeGroupId, Guid sizeId, CancellationToken cancellationToken) =>
        await sizeRepository.FindAsync(sizeGroupId, sizeId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The size was not found.");
}
