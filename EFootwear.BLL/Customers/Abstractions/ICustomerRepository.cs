using EFootwear.BLL.Common;
using EFootwear.BLL.Customers.Contracts;

namespace EFootwear.BLL.Customers.Abstractions;

/// <summary>
/// Data access for customers. Implemented in EFootwear.DAL/Customers/Repositories/CustomerRepository.cs. Soft-deleted customers are never returned.
/// </summary>
public interface ICustomerRepository
{
    Task<PagedResult<CustomerListItemResponse>> SearchAsync(CustomerSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerLookupItemResponse>> LookupAsync(string? searchText, int maximumItemCount, CancellationToken cancellationToken);
    Task<CustomerDetailResponse?> FindDetailAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// The oldest active customer with this name (capital letters do not matter) and this phone number (spaces, dashes and brackets do not matter),
    /// or null. The walk-in customer never matches.
    /// </summary>
    Task<Guid?> FindActiveIdByNameAndPhoneAsync(string name, string phone, CancellationToken cancellationToken);

    /// <summary>Inserts the customer and saves, so later steps in the same transaction can refer to its id.</summary>
    Task<Guid> AddAsync(CustomerWriteRecord record, string code, Guid createdBy, CancellationToken cancellationToken);

    Task UpdateAsync(Guid customerId, CustomerWriteRecord record, long rowVersion, CancellationToken cancellationToken);
    Task SoftDeleteAsync(Guid customerId, Guid deletedBy, CancellationToken cancellationToken);
}
