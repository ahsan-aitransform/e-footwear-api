using EFootwear.BLL.Commissions.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Commissions.Abstractions;

/// <summary>
/// The commission entries (hr.commission_entries): read, and approve through hr.approve_commissions. Entries are created and reversed by the database when
/// a sale is posted, returned or voided, never by this API. The tables are isolated by company and store; the queries also name the caller's stores.
/// </summary>
public interface ICommissionEntryRepository
{
    Task<PagedResult<CommissionEntryResponse>> SearchAsync(CommissionEntrySearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken);

    Task<CommissionEntryTotalsResponse> GetTotalsAsync(CommissionEntrySearchRequest filter, CancellationToken cancellationToken);

    /// <summary>Approves the pending entries of a store and range of sale dates (hr.approve_commissions). Returns how many were approved.</summary>
    Task<int> ApproveAsync(Guid storeId, DateOnly fromDate, DateOnly toDate, Guid userId, CancellationToken cancellationToken);
}
