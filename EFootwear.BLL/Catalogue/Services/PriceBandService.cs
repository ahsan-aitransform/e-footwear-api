using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Catalogue.Services;

/// <summary>
/// Price bands (catalog.price_bands, Part 20.3 and the sale reports by price): named ranges of list prices that the owner defines.
/// A band is valid when its name is set and its range is sensible; the database refuses ranges that overlap another band of the company.
/// </summary>
public sealed class PriceBandService(
    IPriceBandRepository priceBandRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<PriceBandService> logger)
{
    public Task<IReadOnlyList<PriceBandResponse>> ListAsync(CancellationToken cancellationToken) => priceBandRepository.ListAsync(cancellationToken);

    public async Task<PriceBandResponse> GetAsync(Guid priceBandId, CancellationToken cancellationToken) =>
        await priceBandRepository.FindAsync(priceBandId, cancellationToken) ?? throw new DomainException(ErrorKind.NotFound, "The price band was not found.");

    public async Task<CreatedIdResponse> CreateAsync(SavePriceBandRequest request, CancellationToken cancellationToken)
    {
        var name = PriceBandRules.NormalizeName(request.Name);
        PriceBandRules.EnsureValid(name, request.MinimumPrice, request.MaximumPrice, request.SortOrder);
        var priceBandId = await priceBandRepository.AddAsync(name, request.MinimumPrice, request.MaximumPrice, request.SortOrder, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Price band {PriceBandId} created in company {CompanyId} by user {UserId}", priceBandId, currentUser.CompanyId, currentUser.UserId);
        return new CreatedIdResponse(priceBandId);
    }

    public async Task UpdateAsync(Guid priceBandId, SavePriceBandRequest request, CancellationToken cancellationToken)
    {
        _ = await GetAsync(priceBandId, cancellationToken);
        if (request.RowVersion is not { } rowVersion)
            throw new DomainException(ErrorKind.Validation, "The row version is missing; reload the price band.");
        var name = PriceBandRules.NormalizeName(request.Name);
        PriceBandRules.EnsureValid(name, request.MinimumPrice, request.MaximumPrice, request.SortOrder);
        await priceBandRepository.UpdateAsync(priceBandId, name, request.MinimumPrice, request.MaximumPrice, request.SortOrder, rowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid priceBandId, CancellationToken cancellationToken)
    {
        _ = await GetAsync(priceBandId, cancellationToken);
        await priceBandRepository.SoftDeleteAsync(priceBandId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Price band {PriceBandId} deleted in company {CompanyId} by user {UserId}", priceBandId, currentUser.CompanyId, currentUser.UserId);
    }
}
