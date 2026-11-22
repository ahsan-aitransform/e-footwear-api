using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.Domain.Common;

namespace EFootwear.BLL.Accounting.Services;

/// <summary>Cash and bank account balances as on a date (cash management, Part 15). Read-only; the balances are derived from the journal.</summary>
public sealed class CashAndBankBalanceService(ICashAndBankBalanceRepository cashAndBankBalanceRepository, ICurrentUser currentUser)
{
    public Task<CashAndBankBalancesResponse> GetAsync(DateOnly asOfDate, Guid? storeId, CancellationToken cancellationToken)
    {
        if (storeId is { } id && !currentUser.CanAccessStore(id))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
        return cashAndBankBalanceRepository.GetAsync(asOfDate, storeId, cancellationToken);
    }
}
