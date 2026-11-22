using EFootwear.BLL.Abstractions;
using EFootwear.BLL.CashManagement.Abstractions;
using EFootwear.BLL.CashManagement.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.CashManagement;
using EFootwear.Domain.Common;
using EFootwear.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.CashManagement.Repositories;

/// <summary>Data access for customer promises (cash.promises) joined with the customer name. A promise is never deleted: cancelling sets its status.</summary>
public sealed class PromiseRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IPromiseRepository
{
    private static readonly SortableFields<PromiseRow> SortablePromiseFields = new SortableFields<PromiseRow>()
        .Allow("dueDate", row => row.Promise.DueDate)
        .Allow("promiseDate", row => row.Promise.PromiseDate)
        .Allow("customer", row => row.CustomerName)
        .Allow("amount", row => row.Promise.PromisedAmount)
        .Allow("number", row => row.Promise.PromiseNumber);

    public async Task<PromiseListResponse> SearchAsync(PromiseSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cancellationToken);
        if (filter.StoreId is { } storeId) rows = rows.Where(row => row.Promise.StoreId == storeId);
        if (filter.From is { } fromDate) rows = rows.Where(row => row.Promise.DueDate >= fromDate);
        if (filter.To is { } toDate) rows = rows.Where(row => row.Promise.DueDate <= toDate);
        if (pageRequest.SearchText is { } searchText)
        {
            var pattern = LikePattern.Contains(searchText);
            rows = rows.Where(row => EF.Functions.ILike(row.CustomerName, pattern, LikePattern.EscapeCharacter)
                                     || (row.Promise.Note != null && EF.Functions.ILike(row.Promise.Note, pattern, LikePattern.EscapeCharacter)));
        }

        var totalItemCount = await rows.LongCountAsync(cancellationToken);
        var countedRows = rows.Where(row => row.Promise.Status != PromiseStatusCodes.Cancelled);
        var totalPromised = await countedRows.SumAsync(row => (long?)row.Promise.PromisedAmount, cancellationToken) ?? 0;
        var totalPaid = await countedRows.SumAsync(row => (long?)row.Promise.PaidAmount, cancellationToken) ?? 0;

        var page = await rows.ApplySortingAndPaging(pageRequest, SortablePromiseFields, row => row.Promise.Id).ToListAsync(cancellationToken);
        return new PromiseListResponse(
            new PagedResult<PromiseResponse>(page.Select(ToResponse).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalItemCount),
            totalPromised, totalPaid, totalPromised - totalPaid);
    }

    public async Task<PromiseResponse?> FindAsync(Guid promiseId, CancellationToken cancellationToken)
    {
        var row = await (await RowsAsync(cancellationToken)).Where(candidate => candidate.Promise.Id == promiseId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToResponse(row);
    }

    public async Task<Guid> AddAsync(CreatePromiseRequest request, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var storeId = request.StoreId;
        var promiseNumber = await databaseContext.Database
            .SqlQuery<long>($"select core.next_number({companyId}, {storeId}, {PromiseRules.NumberSeriesCode}) as \"Value\"")
            .SingleAsync(cancellationToken);

        var promise = new CashPromise
        {
            CompanyId = companyId,
            PromiseNumber = promiseNumber,
            StoreId = request.StoreId,
            CustomerPartyId = request.CustomerPartyId,
            PromiseDate = request.PromiseDate,
            DueDate = request.DueDate,
            PromisedAmount = request.PromisedAmount,
            Note = request.Note,
            CreatedBy = createdBy,
        };
        databaseContext.CashPromises.Add(promise);
        await databaseContext.SaveChangesAsync(cancellationToken);
        return promise.Id;
    }

    public async Task UpdateAsync(Guid promiseId, DateOnly dueDate, long promisedAmount, string? note, long rowVersion, CancellationToken cancellationToken)
    {
        var promise = await RequireTrackedPromiseAsync(promiseId, cancellationToken);
        promise.DueDate = dueDate;
        promise.PromisedAmount = promisedAmount;
        promise.Note = note;
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.Entry(promise).Property(entity => entity.RowVersion).OriginalValue = rowVersion;
    }

    public async Task CancelAsync(Guid promiseId, long rowVersion, CancellationToken cancellationToken)
    {
        var promise = await RequireTrackedPromiseAsync(promiseId, cancellationToken);
        promise.Status = PromiseStatusCodes.Cancelled;
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        databaseContext.Entry(promise).Property(entity => entity.RowVersion).OriginalValue = rowVersion;
    }

    public async Task<CustomerBalanceResponse?> GetCustomerBalanceAsync(Guid customerPartyId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var customer = await (from party in databaseContext.Parties.AsNoTracking()
                              where party.CompanyId == companyId && party.Id == customerPartyId && !party.IsDeleted && party.IsCustomer
                              join balance in databaseContext.PartyBalances.AsNoTracking() on new { party.CompanyId, PartyId = party.Id } equals new { balance.CompanyId, balance.PartyId } into balanceGroup
                              from balance in balanceGroup.DefaultIfEmpty()
                              select new { party.Id, party.Name, BalanceAmount = balance == null ? 0 : balance.Balance })
            .SingleOrDefaultAsync(cancellationToken);
        return customer is null ? null : new CustomerBalanceResponse(customer.Id, customer.Name, customer.BalanceAmount);
    }

    public async Task<bool> IsActiveCustomerAsync(Guid customerPartyId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Parties.AsNoTracking().AnyAsync(party =>
            party.CompanyId == companyId && party.Id == customerPartyId && !party.IsDeleted && party.IsCustomer && party.Status == PartyStatusCodes.Active, cancellationToken);
    }

    private async Task<IQueryable<PromiseRow>> RowsAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return from promise in databaseContext.CashPromises.AsNoTracking()
               where promise.CompanyId == companyId
               join party in databaseContext.Parties.AsNoTracking() on new { promise.CompanyId, Id = promise.CustomerPartyId } equals new { party.CompanyId, party.Id }
               select new PromiseRow { Promise = promise, CustomerName = party.Name };
    }

    private async Task<CashPromise> RequireTrackedPromiseAsync(Guid promiseId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.CashPromises.SingleOrDefaultAsync(promise => promise.CompanyId == companyId && promise.Id == promiseId, cancellationToken)
               ?? throw new DomainException(ErrorKind.NotFound, "The promise was not found.");
    }

    private static PromiseResponse ToResponse(PromiseRow row) => new(
        row.Promise.Id, row.Promise.PromiseNumber, row.Promise.StoreId, row.Promise.CustomerPartyId, row.CustomerName,
        row.Promise.PromiseDate, row.Promise.DueDate, row.Promise.PromisedAmount, row.Promise.PaidAmount,
        row.Promise.PromisedAmount - row.Promise.PaidAmount, row.Promise.Status, row.Promise.Note, row.Promise.RowVersion);

    private sealed class PromiseRow
    {
        public CashPromise Promise { get; set; } = null!;
        public string CustomerName { get; set; } = "";
    }
}
