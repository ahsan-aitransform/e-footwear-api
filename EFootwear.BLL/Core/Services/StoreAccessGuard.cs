using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.Domain.Common;

namespace EFootwear.BLL.Core.Services;

/// <summary>Checks that a store id from a request is a real store of the caller's company that the caller may work in.</summary>
public sealed class StoreAccessGuard(RequestContext requestContext, IStoreRepository storeRepository)
{
    public async Task EnsureAccessibleAsync(Guid storeId, CancellationToken cancellationToken)
    {
        if (storeId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, "Choose a store (the head office has no store settings).");
        if (!requestContext.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
        var accessibleStores = await storeRepository.ListAccessibleAsync(cancellationToken);
        if (accessibleStores.All(store => store.Id != storeId))
            throw new DomainException(ErrorKind.NotFound, "Store not found.");
    }
}
