using EFootwear.BLL.Core.Contracts;

namespace EFootwear.BLL.Core.Abstractions;

/// <summary>Read-only view of core.devices (the Security module registers and authorizes devices).</summary>
public interface IPosTerminalRepository
{
    Task<IReadOnlyList<PosTerminalResponse>> ListAsync(Guid companyId, Guid? storeId, CancellationToken cancellationToken);
}
