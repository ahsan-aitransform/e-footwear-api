using EFootwear.BLL.Abstractions;
using EFootwear.BLL.CashManagement.Abstractions;
using EFootwear.BLL.CashManagement.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.CashManagement;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.CashManagement.Repositories;

/// <summary>
/// Data access for cash counts (cash.cash_counts with cash.count_denominations). The expected cash is read from the journal
/// (the cash accounts' lines posted in the store), never typed in. One count exists per store and day.
/// </summary>
public sealed class CashCountRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : ICashCountRepository
{
    private static readonly SortableFields<CountRow> SortableCountFields = new SortableFields<CountRow>()
        .Allow("date", row => row.Count.CountDate)
        .Allow("store", row => row.StoreName)
        .Allow("difference", row => row.Count.DifferenceAmount);

    public async Task<long> GetExpectedCashAsync(Guid storeId, DateOnly countDate, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        // The cash accounts are looked up by system role; sales post to the head-office cash account with the store on the line,
        // so the store is taken from the journal line, not from the account.
        return await databaseContext.Database.SqlQuery<long>($"""
            select coalesce(sum(jl.debit - jl.credit), 0)::bigint as "Value"
              from acct.journal_lines jl
              join acct.accounts a on a.company_id = jl.company_id and a.id = jl.account_id
             where jl.company_id = {companyId} and a.system_role = 'CASH' and jl.store_id = {storeId} and jl.entry_date <= {countDate}
            """).SingleAsync(cancellationToken);
    }

    public async Task<CashCountResponse?> FindByStoreAndDateAsync(Guid storeId, DateOnly countDate, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var row = await (from count in databaseContext.CashCounts.AsNoTracking()
                         where count.CompanyId == companyId && count.StoreId == storeId && count.CountDate == countDate
                         join user in databaseContext.CompanyUsers.AsNoTracking() on new { count.CompanyId, Id = count.CountedBy } equals new { user.CompanyId, Id = (Guid?)user.Id } into userGroup
                         from user in userGroup.DefaultIfEmpty()
                         select new { Count = count, CountedByName = user == null ? null : user.FullName })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var denominations = await databaseContext.CashCountDenominations.AsNoTracking()
            .Where(line => line.CompanyId == companyId && line.CountId == row.Count.Id)
            .OrderByDescending(line => line.Denomination)
            .Select(line => new CashCountDenominationLine(line.Denomination, line.NoteCount))
            .ToListAsync(cancellationToken);
        var savedCount = row.Count;
        return new CashCountResponse(savedCount.Id, savedCount.StoreId, savedCount.CountDate, savedCount.OpeningCash, savedCount.ExpectedCash,
                                     savedCount.CountedCash, savedCount.DifferenceAmount, denominations, savedCount.PaymentsAmount,
                                     savedCount.CreditSalesAmount, savedCount.CreditReceivedAmount, savedCount.Note, savedCount.CountedBy,
                                     row.CountedByName, savedCount.CreatedAt, savedCount.RowVersion);
    }

    public async Task<PagedResult<CashCountListItemResponse>> ListAsync(CashCountSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = from count in databaseContext.CashCounts.AsNoTracking()
                   where count.CompanyId == companyId
                   join store in databaseContext.Stores.AsNoTracking() on new { count.CompanyId, Id = count.StoreId } equals new { store.CompanyId, store.Id }
                   join user in databaseContext.CompanyUsers.AsNoTracking() on new { count.CompanyId, Id = count.CountedBy } equals new { user.CompanyId, Id = (Guid?)user.Id } into userGroup
                   from user in userGroup.DefaultIfEmpty()
                   select new CountRow { Count = count, StoreName = store.Name, CountedByName = user == null ? null : user.FullName };
        if (filter.StoreId is { } storeId) rows = rows.Where(row => row.Count.StoreId == storeId);
        if (filter.From is { } fromDate) rows = rows.Where(row => row.Count.CountDate >= fromDate);
        if (filter.To is { } toDate) rows = rows.Where(row => row.Count.CountDate <= toDate);

        var totalItemCount = await rows.LongCountAsync(cancellationToken);
        var page = await rows.ApplySortingAndPaging(pageRequest, SortableCountFields, row => row.Count.Id).ToListAsync(cancellationToken);
        return new PagedResult<CashCountListItemResponse>(
            page.Select(row => new CashCountListItemResponse(row.Count.Id, row.Count.StoreId, row.StoreName, row.Count.CountDate, row.Count.ExpectedCash,
                                                             row.Count.CountedCash, row.Count.DifferenceAmount, row.CountedByName, row.Count.CreatedAt)).ToList(),
            pageRequest.PageNumber, pageRequest.PageSize, totalItemCount);
    }

    public async Task<Guid> SaveAsync(SaveCashCountRequest request, long countedCash, long expectedCash, Guid countedBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var existing = await databaseContext.CashCounts.SingleOrDefaultAsync(count =>
            count.CompanyId == companyId && count.StoreId == request.StoreId && count.CountDate == request.CountDate, cancellationToken);

        var count = existing;
        if (count is null)
        {
            count = new CashCount { CompanyId = companyId, StoreId = request.StoreId, CountDate = request.CountDate, CreatedBy = countedBy };
            databaseContext.CashCounts.Add(count);
        }
        else
        {
            if (request.RowVersion is null)
                throw new DomainException(ErrorKind.Conflict, "A cash count already exists for this store and day. Open it again to change it.");
            databaseContext.Entry(count).Property(entity => entity.RowVersion).OriginalValue = request.RowVersion.Value;
        }

        count.OpeningCash = request.OpeningCash;
        count.ExpectedCash = expectedCash;
        count.CountedCash = countedCash;
        count.CountedBy = countedBy;
        count.Note = request.Note;
        count.PaymentsAmount = request.PaymentsAmount;
        count.CreditSalesAmount = request.CreditSalesAmount;
        count.CreditReceivedAmount = request.CreditReceivedAmount;
        await databaseContext.SaveChangesAsync(cancellationToken);    // creates the row, or checks the row version and updates it

        await databaseContext.CashCountDenominations.Where(line => line.CompanyId == companyId && line.CountId == count.Id).ExecuteDeleteAsync(cancellationToken);
        databaseContext.CashCountDenominations.AddRange(request.Denominations
            .Where(line => line.NoteCount > 0)
            .Select(line => new CashCountDenomination { CompanyId = companyId, CountId = count.Id, Denomination = line.Denomination, NoteCount = line.NoteCount }));
        return count.Id;
    }

    private sealed class CountRow
    {
        public CashCount Count { get; set; } = null!;
        public string StoreName { get; set; } = "";
        public string? CountedByName { get; set; }
    }
}
