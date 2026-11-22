using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>Price bands (catalog.price_bands). Implemented in EFootwear.DAL/Catalogue/Repositories/PriceBandRepository.cs. Overlaps are refused by the database.</summary>
public interface IPriceBandRepository
{
    Task<IReadOnlyList<PriceBandResponse>> ListAsync(CancellationToken cancellationToken);
    Task<PriceBandResponse?> FindAsync(Guid priceBandId, CancellationToken cancellationToken);
    Task<Guid> AddAsync(string name, long minimumPrice, long? maximumPrice, int sortOrder, Guid createdBy, CancellationToken cancellationToken);
    Task UpdateAsync(Guid priceBandId, string name, long minimumPrice, long? maximumPrice, int sortOrder, long rowVersion, CancellationToken cancellationToken);
    Task SoftDeleteAsync(Guid priceBandId, CancellationToken cancellationToken);
}
