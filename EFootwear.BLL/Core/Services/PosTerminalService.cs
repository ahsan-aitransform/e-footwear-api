using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;

namespace EFootwear.BLL.Core.Services;

/// <summary>Read-only list of the POS terminals (devices) of the stores the caller works in. Registration belongs to the Security module.</summary>
public sealed class PosTerminalService(
    IPosTerminalRepository posTerminalRepository,
    StoreAccessGuard storeAccessGuard,
    RequestContext requestContext)
{
    public async Task<IReadOnlyList<PosTerminalResponse>> ListAsync(Guid? storeId, CancellationToken cancellationToken)
    {
        if (storeId is { } requestedStoreId) await storeAccessGuard.EnsureAccessibleAsync(requestedStoreId, cancellationToken);
        return await posTerminalRepository.ListAsync(requestContext.CompanyId, storeId, cancellationToken);
    }
}
