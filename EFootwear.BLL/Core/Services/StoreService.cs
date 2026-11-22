using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;

namespace EFootwear.BLL.Core.Services;

/// <summary>
/// Read-only store use case for company users. Limits (own company, assigned stores) are enforced by the
/// database through row-level security and core.store_allowed().
/// </summary>
public sealed class StoreService(IStoreRepository storeRepository)
{
    public Task<IReadOnlyList<StoreResponse>> ListAsync(CancellationToken cancellationToken) =>
        storeRepository.ListAccessibleAsync(cancellationToken);
}
