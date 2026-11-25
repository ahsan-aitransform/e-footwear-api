using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Contracts;
using EFootwear.BLL.Vendors.Abstractions;
using EFootwear.BLL.Vendors.Contracts;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;
using EFootwear.Domain.MasterData;
using EFootwear.Domain.Vendors;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Vendors.Repositories;

/// <summary>
/// Data access for vendors. Every query is limited to the caller's company (row-level security repeats it) and hides soft-deleted vendors.
/// The balance comes from contacts.vendor_balances, which the database keeps in step with the journal: nothing here writes a balance.
/// </summary>
public sealed class VendorRepository(DatabaseSession databaseSession, ICurrentUser currentUser, IDateTimeProvider dateTimeProvider) : IVendorRepository
{
    private static readonly SortableFields<VendorRow> SortableVendorFields = new SortableFields<VendorRow>()
        .Allow("name", row => row.Vendor.Name)
        .Allow("code", row => row.Vendor.Code)
        .Allow("city", row => row.CityName)
        .Allow("balance", row => row.BalanceAmount);

    public async Task<PagedResult<VendorListItemResponse>> SearchAsync(VendorSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cancellationToken);
        if (filter.CityId is { } cityId) rows = rows.Where(row => row.Vendor.CityId == cityId);
        if (filter.StatusCode is { } statusCode) rows = rows.Where(row => row.Vendor.Status == statusCode);
        if (pageRequest.SearchText is { } searchText) rows = rows.Where(MatchesSearchText(searchText));

        var totalItemCount = await rows.LongCountAsync(cancellationToken);
        var page = await rows.ApplySortingAndPaging(pageRequest, SortableVendorFields, row => row.Vendor.Id).ToListAsync(cancellationToken);
        return new PagedResult<VendorListItemResponse>(page.Select(ToListItem).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalItemCount);
    }

    public async Task<IReadOnlyList<VendorLookupItemResponse>> LookupAsync(string? searchText, int maximumItemCount, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var vendors = databaseContext.Vendors.AsNoTracking()
            .Where(vendor => vendor.CompanyId == companyId && !vendor.IsDeleted && vendor.Status == ContactStatusCodes.Active);
        if (searchText is not null)
        {
            var pattern = LikePattern.Contains(searchText);
            vendors = vendors.Where(vendor => EF.Functions.ILike(vendor.Name, pattern, LikePattern.EscapeCharacter)
                                                  || EF.Functions.ILike(vendor.Code, pattern, LikePattern.EscapeCharacter)
                                                  || (vendor.Phone != null && EF.Functions.ILike(vendor.Phone, pattern, LikePattern.EscapeCharacter)));
        }

        return await vendors.OrderBy(vendor => vendor.Name).ThenBy(vendor => vendor.Id).Take(maximumItemCount)
            .Select(vendor => new VendorLookupItemResponse(vendor.Id, vendor.Code, vendor.Name, vendor.Phone))
            .ToListAsync(cancellationToken);
    }

    public async Task<VendorDetailResponse?> FindDetailAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var row = await (await RowsAsync(cancellationToken)).Where(candidate => candidate.Vendor.Id == vendorId).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var openingBalance = await databaseContext.Database.SqlQuery<OpeningBalanceRow>($"""
            select entry_date as "AsOfDate", debit as "DebitAmount", credit as "CreditAmount"
              from acct.v_live_opening_balances
             where company_id = {companyId} and vendor_id = {vendorId}
            """).ToListAsync(cancellationToken);
        var opening = openingBalance.FirstOrDefault();

        var vendor = row.Vendor;
        return new VendorDetailResponse(
            vendor.Id, vendor.Code, vendor.Name, vendor.Phone, vendor.Email, vendor.BirthDate, vendor.CityId, row.CityName, vendor.CreditLimit, vendor.Status,
            vendor.CreatedAt, row.BalanceAmount,
            opening is null ? null : new ContactOpeningBalanceResponse(
                opening.DebitAmount > 0 ? OpeningBalanceSideCodes.Debit : OpeningBalanceSideCodes.Credit,
                opening.DebitAmount > 0 ? opening.DebitAmount : opening.CreditAmount, opening.AsOfDate),
            vendor.RowVersion);
    }

    public async Task<Guid> AddAsync(VendorWriteRecord record, string code, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var vendor = new Vendor
        {
            CompanyId = currentUser.CompanyId,
            Code = code,
            CreatedBy = createdBy,
        };
        ApplyFields(vendor, record);
        databaseContext.Vendors.Add(vendor);
        await databaseContext.SaveChangesAsync(cancellationToken);   // the database generates the id; later steps need it
        return vendor.Id;
    }

    public async Task UpdateAsync(Guid vendorId, VendorWriteRecord record, long rowVersion, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var vendor = await databaseContext.Vendors.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == vendorId && !candidate.IsDeleted, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The vendor was not found.");
        ApplyFields(vendor, record);
        databaseContext.Entry(vendor).Property(entity => entity.RowVersion).OriginalValue = rowVersion;
    }

    public async Task SoftDeleteAsync(Guid vendorId, Guid deletedBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var vendor = await databaseContext.Vendors.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == vendorId && !candidate.IsDeleted, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The vendor was not found.");
        vendor.IsDeleted = true;
        vendor.DeletedAt = dateTimeProvider.UtcNow;
        vendor.DeletedBy = deletedBy;
    }

    // ---------------------------------------------------------------- helpers

    private static void ApplyFields(Vendor vendor, VendorWriteRecord record)
    {
        vendor.Name = record.Name;
        vendor.Phone = record.Phone;
        vendor.Email = record.Email;
        vendor.BirthDate = record.BirthDate;
        vendor.CityId = record.CityId;
        vendor.CreditLimit = record.CreditLimit;
        vendor.Status = record.StatusCode;
    }

    private async Task<IQueryable<VendorRow>> RowsAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var cities = databaseContext.Set<MasterListItem>(MasterListDefinitions.City.EntityName).AsNoTracking();
        return from vendor in databaseContext.Vendors.AsNoTracking()
               where vendor.CompanyId == companyId && !vendor.IsDeleted
               join city in cities on new { vendor.CompanyId, Id = vendor.CityId } equals new { city.CompanyId, Id = (Guid?)city.Id } into cityGroup
               from city in cityGroup.DefaultIfEmpty()
               join balance in databaseContext.VendorBalances.AsNoTracking() on new { vendor.CompanyId, VendorId = vendor.Id } equals new { balance.CompanyId, balance.VendorId } into balanceGroup
               from balance in balanceGroup.DefaultIfEmpty()
               select new VendorRow
               {
                   Vendor = vendor,
                   CityName = city == null ? null : city.Name,
                   BalanceAmount = balance == null ? 0 : balance.Balance,
               };
    }

    private static System.Linq.Expressions.Expression<Func<VendorRow, bool>> MatchesSearchText(string searchText)
    {
        var pattern = LikePattern.Contains(searchText);
        return row => EF.Functions.ILike(row.Vendor.Name, pattern, LikePattern.EscapeCharacter)
                      || EF.Functions.ILike(row.Vendor.Code, pattern, LikePattern.EscapeCharacter)
                      || (row.Vendor.Phone != null && EF.Functions.ILike(row.Vendor.Phone, pattern, LikePattern.EscapeCharacter))
                      || (row.Vendor.Email != null && EF.Functions.ILike(row.Vendor.Email, pattern, LikePattern.EscapeCharacter))
                      || (row.CityName != null && EF.Functions.ILike(row.CityName, pattern, LikePattern.EscapeCharacter));
    }

    private static VendorListItemResponse ToListItem(VendorRow row) => new(
        row.Vendor.Id, row.Vendor.Code, row.Vendor.Name, row.Vendor.Phone, row.Vendor.Email, row.Vendor.BirthDate, row.Vendor.CityId, row.CityName, row.Vendor.CreditLimit,
        row.Vendor.Status, row.BalanceAmount, row.Vendor.RowVersion);

    private sealed class VendorRow
    {
        public Vendor Vendor { get; set; } = null!;
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
