using EFootwear.BLL.Accounting.Contracts;

namespace EFootwear.BLL.Accounting.Abstractions;

/// <summary>Cash and bank account balances as on a date, derived from journal lines (cash management, Part 15). Read-only.</summary>
public interface ICashAndBankBalanceRepository
{
    Task<CashAndBankBalancesResponse> GetAsync(DateOnly asOfDate, Guid? storeId, CancellationToken cancellationToken);
}
