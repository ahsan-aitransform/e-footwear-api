using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Accounting.Services;

/// <summary>
/// Closing and reopening months of the books. A closed month refuses new journal entries dated in it (trigger acct.check_period_open); a void
/// done later is dated today, so it is not blocked by the closed month of the original entry. Who may close a month, and when, is a business policy
/// the schema does not define ([BUSINESS DECISION REQUIRED]): nothing closes a month automatically and the screen needs the Approve permission.
/// The only technical guard: a month that has not started yet cannot be closed.
/// </summary>
public sealed class AccountingPeriodService(
    IAccountingPeriodRepository accountingPeriodRepository,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IUnitOfWork unitOfWork,
    ILogger<AccountingPeriodService> logger)
{
    public Task<IReadOnlyList<AccountingPeriodResponse>> ListAsync(CancellationToken cancellationToken) =>
        accountingPeriodRepository.ListAsync(cancellationToken);

    public async Task<AccountingPeriodResponse> CloseAsync(ChangeAccountingPeriodRequest request, CancellationToken cancellationToken)
    {
        var periodStart = FirstDayOfMonth(request.PeriodStart);
        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow.UtcDateTime);
        if (periodStart > FirstDayOfMonth(today))
            throw new DomainException(ErrorKind.Validation, "A month that has not started yet cannot be closed.");
        return await SetStatusAsync(periodStart, AccountingPeriodStatusCodes.Closed, cancellationToken);
    }

    public Task<AccountingPeriodResponse> ReopenAsync(ChangeAccountingPeriodRequest request, CancellationToken cancellationToken) =>
        SetStatusAsync(FirstDayOfMonth(request.PeriodStart), AccountingPeriodStatusCodes.Open, cancellationToken);

    private async Task<AccountingPeriodResponse> SetStatusAsync(DateOnly periodStart, string statusCode, CancellationToken cancellationToken)
    {
        var period = await accountingPeriodRepository.SetStatusAsync(periodStart, statusCode, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Accounting period {PeriodStart} set to {Status} in company {CompanyId} by user {UserId}",
            periodStart, statusCode, currentUser.CompanyId, currentUser.UserId);
        return period;
    }

    private static DateOnly FirstDayOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
