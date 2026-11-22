using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Accounting.Repositories;

/// <summary>Months of the books (acct.periods). A month without a row is open; closing writes a row, reopening sets it back to open.</summary>
public sealed class AccountingPeriodRepository(DatabaseSession databaseSession, ICurrentUser currentUser, IDateTimeProvider dateTimeProvider)
    : IAccountingPeriodRepository
{
    public async Task<IReadOnlyList<AccountingPeriodResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        // Every month from the first journal entry (or the oldest stored period, or this month) to the current month.
        var rows = await databaseContext.Database.SqlQuery<PeriodRow>($"""
            with bounds as (
                select least(
                         coalesce((select date_trunc('month', min(entry_date))::date from acct.journal_entries where company_id = {companyId}),
                                  date_trunc('month', current_date)::date),
                         coalesce((select min(period_start) from acct.periods where company_id = {companyId}),
                                  date_trunc('month', current_date)::date)) as first_month
            ), months as (
                select generate_series(b.first_month, date_trunc('month', current_date)::date, interval '1 month')::date as period_start
                  from bounds b
            )
            select m.period_start as "PeriodStart", coalesce(p.status, 'OPEN') as "StatusCode", p.closed_at as "ClosedAt", u.full_name as "ClosedByName"
              from months m
              left join acct.periods p on p.company_id = {companyId} and p.period_start = m.period_start
              left join sec.users u on u.company_id = {companyId} and u.id = p.closed_by
             order by m.period_start desc
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new AccountingPeriodResponse(row.PeriodStart, row.StatusCode, row.ClosedAt, row.ClosedByName)).ToList();
    }

    public async Task<AccountingPeriodResponse> SetStatusAsync(DateOnly periodStart, string statusCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        var period = await databaseContext.AccountingPeriods.FindAsync([companyId, periodStart], cancellationToken);
        if (period is null)
        {
            period = new AccountingPeriod { CompanyId = companyId, PeriodStart = periodStart };
            databaseContext.AccountingPeriods.Add(period);
        }

        period.Status = statusCode;
        if (statusCode == AccountingPeriodStatusCodes.Closed)
        {
            period.ClosedBy = currentUser.UserId;
            period.ClosedAt = dateTimeProvider.UtcNow;
        }
        else
        {
            period.ClosedBy = null;
            period.ClosedAt = null;
        }

        var closedByName = period.ClosedBy is { } closedBy
            ? await databaseContext.CompanyUsers.AsNoTracking()
                .Where(user => user.CompanyId == companyId && user.Id == closedBy)
                .Select(user => user.FullName).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new AccountingPeriodResponse(period.PeriodStart, period.Status, period.ClosedAt, closedByName);
    }

    private sealed class PeriodRow
    {
        public DateOnly PeriodStart { get; set; }
        public string StatusCode { get; set; } = "";
        public DateTimeOffset? ClosedAt { get; set; }
        public string? ClosedByName { get; set; }
    }
}
