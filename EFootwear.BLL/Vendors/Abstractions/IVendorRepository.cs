using EFootwear.BLL.Common;
using EFootwear.BLL.Vendors.Contracts;

namespace EFootwear.BLL.Vendors.Abstractions;

/// <summary>
/// Data access for vendors. Implemented in EFootwear.DAL/Vendors/Repositories/VendorRepository.cs. Soft-deleted vendors are never returned.
/// </summary>
public interface IVendorRepository
{
    Task<PagedResult<VendorListItemResponse>> SearchAsync(VendorSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorLookupItemResponse>> LookupAsync(string? searchText, int maximumItemCount, CancellationToken cancellationToken);
    Task<VendorDetailResponse?> FindDetailAsync(Guid vendorId, CancellationToken cancellationToken);

    /// <summary>Inserts the vendor and saves, so later steps in the same transaction can refer to its id.</summary>
    Task<Guid> AddAsync(VendorWriteRecord record, string code, Guid createdBy, CancellationToken cancellationToken);

    Task UpdateAsync(Guid vendorId, VendorWriteRecord record, long rowVersion, CancellationToken cancellationToken);
    Task SoftDeleteAsync(Guid vendorId, Guid deletedBy, CancellationToken cancellationToken);
}
