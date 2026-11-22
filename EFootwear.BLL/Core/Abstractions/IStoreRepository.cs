using EFootwear.BLL.Core.Contracts;

namespace EFootwear.BLL.Core.Abstractions;

/// <summary>
/// BLL contract for the data layer. The real version lives in EFootwear.DAL/Core/Repositories/StoreRepository.cs.
/// Methods that take a companyId are used by the platform administrator, who works on one company at a time.
/// </summary>
public interface IStoreRepository
{
    /// <summary>Stores of the caller's company that the caller may access.</summary>
    Task<IReadOnlyList<StoreResponse>> ListAccessibleAsync(CancellationToken cancellationToken);

    /// <summary>All stores of a company, including head office. Platform administrators only.</summary>
    Task<IReadOnlyList<StoreResponse>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(Guid companyId, string code, CancellationToken cancellationToken);

    /// <summary>Creates a store. The schema trigger creates its stock, cash, sales and loss accounts.</summary>
    Task<Guid> CreateAsync(Guid companyId, string code, string name, CancellationToken cancellationToken);

    /// <summary>The given ids that are active, not head office, and belong to the company.</summary>
    Task<IReadOnlyList<Guid>> ActiveSellingStoreIdsAsync(Guid companyId, IReadOnlyCollection<Guid> storeIds,
                                                         CancellationToken cancellationToken);
}
