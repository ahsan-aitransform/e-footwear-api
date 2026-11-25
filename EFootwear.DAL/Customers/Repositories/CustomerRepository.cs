using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Contracts;
using EFootwear.BLL.Customers.Abstractions;
using EFootwear.BLL.Customers.Contracts;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;
using EFootwear.Domain.MasterData;
using EFootwear.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Customers.Repositories;

/// <summary>
/// Data access for customers. Every query is limited to the caller's company (row-level security repeats it) and hides soft-deleted customers.
/// The balance comes from contacts.customer_balances, which the database keeps in step with the journal: nothing here writes a balance.
/// </summary>
public sealed class CustomerRepository(DatabaseSession databaseSession, ICurrentUser currentUser, IDateTimeProvider dateTimeProvider) : ICustomerRepository
{
    private static readonly SortableFields<CustomerRow> SortableCustomerFields = new SortableFields<CustomerRow>()
        .Allow("name", row => row.Customer.Name)
        .Allow("code", row => row.Customer.Code)
        .Allow("city", row => row.CityName)
        .Allow("balance", row => row.BalanceAmount);

    public async Task<PagedResult<CustomerListItemResponse>> SearchAsync(CustomerSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cancellationToken);
        if (filter.CityId is { } cityId) rows = rows.Where(row => row.Customer.CityId == cityId);
        if (filter.StatusCode is { } statusCode) rows = rows.Where(row => row.Customer.Status == statusCode);
        if (pageRequest.SearchText is { } searchText) rows = rows.Where(MatchesSearchText(searchText));

        var totalItemCount = await rows.LongCountAsync(cancellationToken);
        var page = await rows.ApplySortingAndPaging(pageRequest, SortableCustomerFields, row => row.Customer.Id).ToListAsync(cancellationToken);
        return new PagedResult<CustomerListItemResponse>(page.Select(ToListItem).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalItemCount);
    }

    public async Task<IReadOnlyList<CustomerLookupItemResponse>> LookupAsync(string? searchText, int maximumItemCount, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var customers = databaseContext.Customers.AsNoTracking()
            .Where(customer => customer.CompanyId == companyId && !customer.IsDeleted && customer.Status == ContactStatusCodes.Active);
        if (searchText is not null)
        {
            var pattern = LikePattern.Contains(searchText);
            customers = customers.Where(customer => EF.Functions.ILike(customer.Name, pattern, LikePattern.EscapeCharacter)
                                                  || EF.Functions.ILike(customer.Code, pattern, LikePattern.EscapeCharacter)
                                                  || (customer.Phone != null && EF.Functions.ILike(customer.Phone, pattern, LikePattern.EscapeCharacter)));
        }

        return await customers.OrderBy(customer => customer.Name).ThenBy(customer => customer.Id).Take(maximumItemCount)
            .Select(customer => new CustomerLookupItemResponse(customer.Id, customer.Code, customer.Name, customer.Phone))
            .ToListAsync(cancellationToken);
    }

    public async Task<CustomerDetailResponse?> FindDetailAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var row = await (await RowsAsync(cancellationToken)).Where(candidate => candidate.Customer.Id == customerId).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var openingBalance = await databaseContext.Database.SqlQuery<OpeningBalanceRow>($"""
            select entry_date as "AsOfDate", debit as "DebitAmount", credit as "CreditAmount"
              from acct.v_live_opening_balances
             where company_id = {companyId} and customer_id = {customerId}
            """).ToListAsync(cancellationToken);
        var opening = openingBalance.FirstOrDefault();

        var customer = row.Customer;
        return new CustomerDetailResponse(
            customer.Id, customer.Code, customer.Name, customer.Phone, customer.Email, customer.BirthDate, customer.CityId, row.CityName, customer.Status,
            customer.CreatedAt, row.BalanceAmount,
            opening is null ? null : new ContactOpeningBalanceResponse(
                opening.DebitAmount > 0 ? OpeningBalanceSideCodes.Debit : OpeningBalanceSideCodes.Credit,
                opening.DebitAmount > 0 ? opening.DebitAmount : opening.CreditAmount, opening.AsOfDate),
            customer.RowVersion);
    }

    public async Task<Guid> AddAsync(CustomerWriteRecord record, string code, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var customer = new Customer
        {
            CompanyId = currentUser.CompanyId,
            Code = code,
            CreatedBy = createdBy,
        };
        ApplyFields(customer, record);
        databaseContext.Customers.Add(customer);
        await databaseContext.SaveChangesAsync(cancellationToken);   // the database generates the id; later steps need it
        return customer.Id;
    }

    public async Task UpdateAsync(Guid customerId, CustomerWriteRecord record, long rowVersion, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var customer = await databaseContext.Customers.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == customerId && !candidate.IsDeleted, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The customer was not found.");
        ApplyFields(customer, record);
        databaseContext.Entry(customer).Property(entity => entity.RowVersion).OriginalValue = rowVersion;
    }

    public async Task SoftDeleteAsync(Guid customerId, Guid deletedBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var customer = await databaseContext.Customers.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == customerId && !candidate.IsDeleted, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The customer was not found.");
        customer.IsDeleted = true;
        customer.DeletedAt = dateTimeProvider.UtcNow;
        customer.DeletedBy = deletedBy;
    }

    // ---------------------------------------------------------------- helpers

    private static void ApplyFields(Customer customer, CustomerWriteRecord record)
    {
        customer.Name = record.Name;
        customer.Phone = record.Phone;
        customer.Email = record.Email;
        customer.BirthDate = record.BirthDate;
        customer.CityId = record.CityId;
        customer.Status = record.StatusCode;
    }

    private async Task<IQueryable<CustomerRow>> RowsAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var cities = databaseContext.Set<MasterListItem>(MasterListDefinitions.City.EntityName).AsNoTracking();
        return from customer in databaseContext.Customers.AsNoTracking()
               where customer.CompanyId == companyId && !customer.IsDeleted
               join city in cities on new { customer.CompanyId, Id = customer.CityId } equals new { city.CompanyId, Id = (Guid?)city.Id } into cityGroup
               from city in cityGroup.DefaultIfEmpty()
               join balance in databaseContext.CustomerBalances.AsNoTracking() on new { customer.CompanyId, CustomerId = customer.Id } equals new { balance.CompanyId, balance.CustomerId } into balanceGroup
               from balance in balanceGroup.DefaultIfEmpty()
               select new CustomerRow
               {
                   Customer = customer,
                   CityName = city == null ? null : city.Name,
                   BalanceAmount = balance == null ? 0 : balance.Balance,
               };
    }

    private static System.Linq.Expressions.Expression<Func<CustomerRow, bool>> MatchesSearchText(string searchText)
    {
        var pattern = LikePattern.Contains(searchText);
        return row => EF.Functions.ILike(row.Customer.Name, pattern, LikePattern.EscapeCharacter)
                      || EF.Functions.ILike(row.Customer.Code, pattern, LikePattern.EscapeCharacter)
                      || (row.Customer.Phone != null && EF.Functions.ILike(row.Customer.Phone, pattern, LikePattern.EscapeCharacter))
                      || (row.Customer.Email != null && EF.Functions.ILike(row.Customer.Email, pattern, LikePattern.EscapeCharacter))
                      || (row.CityName != null && EF.Functions.ILike(row.CityName, pattern, LikePattern.EscapeCharacter));
    }

    private static CustomerListItemResponse ToListItem(CustomerRow row) => new(
        row.Customer.Id, row.Customer.Code, row.Customer.Name, row.Customer.Phone, row.Customer.Email, row.Customer.BirthDate, row.Customer.CityId, row.CityName,
        row.Customer.Status, row.BalanceAmount, row.Customer.RowVersion);

    private sealed class CustomerRow
    {
        public Customer Customer { get; set; } = null!;
        public string? CityName { get; set; }
        public long BalanceAmount { get; set; }
    }

    private sealed class OpeningBalanceRow
    {
        public DateOnly AsOfDate { get; set; }
        public long DebitAmount { get; set; }
        public long CreditAmount { get; set; }
    }
}
