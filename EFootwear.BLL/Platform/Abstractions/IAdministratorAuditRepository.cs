using EFootwear.BLL.Common;
using EFootwear.BLL.Platform.Contracts;

namespace EFootwear.BLL.Platform.Abstractions;

/// <summary>Reads platform.admin_actions, the append-only record of what platform administrators did.</summary>
public interface IAdministratorAuditRepository
{
    Task<PagedResult<AdministratorAuditEntryResponse>> ListAsync(AdministratorAuditQuery query, PageRequest pageRequest,
                                                                 CancellationToken cancellationToken);
}
