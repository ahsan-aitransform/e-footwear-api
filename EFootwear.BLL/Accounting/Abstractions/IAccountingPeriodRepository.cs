using EFootwear.BLL.Accounting.Contracts;

namespace EFootwear.BLL.Accounting.Abstractions;

/// <summary>The months of the books (<c>acct.periods</c>). A month without a row is open; closing writes a row, reopening sets it back to open.</summary>
public interface IAccountingPeriodRepository
{
    /// <summary>Every month from the first journal entry (or the current month when there is none) up to the current month, newest first.</summary>
    Task<IReadOnlyList<AccountingPeriodResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Sets the status of one month. <c>periodStart</c> must be the first day of the month.</summary>
    Task<AccountingPeriodResponse> SetStatusAsync(DateOnly periodStart, string statusCode, CancellationToken cancellationToken);
}
