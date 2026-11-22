using EFootwear.BLL.CashManagement.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.CashManagement.Abstractions;

/// <summary>Data access for end-of-day cash counts (cash.cash_counts and cash.count_denominations). Implemented in EFootwear.DAL/CashManagement/Repositories/CashCountRepository.cs.</summary>
public interface ICashCountRepository
{
    /// <summary>
    /// The cash the books say the store holds at the end of the day: the sum of debit minus credit of the cash accounts' journal lines
    /// posted in this store up to and including the date. Read only, whole rupees.
    /// </summary>
    Task<long> GetExpectedCashAsync(Guid storeId, DateOnly countDate, CancellationToken cancellationToken);

    Task<CashCountResponse?> FindByStoreAndDateAsync(Guid storeId, DateOnly countDate, CancellationToken cancellationToken);
    Task<PagedResult<CashCountListItemResponse>> ListAsync(CashCountSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken);

    /// <summary>Creates the count of the store and day, or replaces the existing one (with its denominations) when it was loaded at the given row version.</summary>
    Task<Guid> SaveAsync(SaveCashCountRequest request, long countedCash, long expectedCash, Guid countedBy, CancellationToken cancellationToken);
}
