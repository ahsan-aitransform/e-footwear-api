namespace EFootwear.BLL.Stores;

public sealed record StoreDto(long Id, string Code, string Name, bool IsHeadOffice, bool IsActive);

/// <summary>
/// BLL contract for the DAL. The real SQL version lives in the DAL (EFootwear.DAL/Stores/StoreRepository.cs).
/// Methods that take a companyId are used by the platform administrator, who works on one company at a time.
/// </summary>
public interface IStoreRepository
{
    /// <summary>Stores of the caller's company that the caller may access.</summary>
    Task<IReadOnlyList<StoreDto>> ListAccessibleAsync(CancellationToken ct);

    /// <summary>All stores of a company, including head office. Platform administrators only.</summary>
    Task<IReadOnlyList<StoreDto>> ListForCompanyAsync(long companyId, CancellationToken ct);

    Task<bool> CodeExistsAsync(long companyId, string code, CancellationToken ct);

    /// <summary>Creates a store. The schema trigger creates its stock, cash, sales and loss accounts.</summary>
    Task<long> CreateAsync(long companyId, string code, string name, CancellationToken ct);

    /// <summary>The given ids that are active, not head office, and belong to the company.</summary>
    Task<IReadOnlyList<long>> ActiveSellingStoreIdsAsync(long companyId, IReadOnlyCollection<long> storeIds, CancellationToken ct);
}

/// <summary>
/// Read-only store use case for company users. Limits (own company, assigned stores) are enforced by the
/// database through row-level security and core.store_allowed().
/// </summary>
public sealed class StoreService(IStoreRepository stores)
{
    public Task<IReadOnlyList<StoreDto>> ListAsync(CancellationToken ct) => stores.ListAccessibleAsync(ct);
}
