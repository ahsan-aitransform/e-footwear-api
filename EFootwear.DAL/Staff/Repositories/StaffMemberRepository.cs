using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Contracts;
using EFootwear.BLL.Staff.Abstractions;
using EFootwear.BLL.Staff.Contracts;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;
using EFootwear.Domain.MasterData;
using EFootwear.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Staff.Repositories;

/// <summary>
/// Data access for staff members and their store history. Every query is limited to the caller's company (row-level security repeats it) and hides
/// soft-deleted staff. The balance comes from contacts.staff_balances, which the database keeps in step with the journal: nothing here writes a balance.
/// </summary>
public sealed class StaffMemberRepository(DatabaseSession databaseSession, ICurrentUser currentUser, IDateTimeProvider dateTimeProvider) : IStaffMemberRepository
{
    private static readonly SortableFields<StaffRow> SortableStaffFields = new SortableFields<StaffRow>()
        .Allow("name", row => row.Staff.Name)
        .Allow("code", row => row.Staff.Code)
        .Allow("city", row => row.CityName)
        .Allow("store", row => row.StoreName)
        .Allow("designation", row => row.Staff.Designation)
        .Allow("balance", row => row.BalanceAmount);

    public async Task<PagedResult<StaffMemberListItemResponse>> SearchAsync(StaffMemberSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cancellationToken);
        if (filter.CityId is { } cityId) rows = rows.Where(row => row.Staff.CityId == cityId);
        if (filter.StoreId is { } storeId) rows = rows.Where(row => row.Staff.CurrentStoreId == storeId);
        if (filter.StatusCode is { } statusCode) rows = rows.Where(row => row.Staff.Status == statusCode);
        if (pageRequest.SearchText is { } searchText) rows = rows.Where(MatchesSearchText(searchText));

        var totalItemCount = await rows.LongCountAsync(cancellationToken);
        var page = await rows.ApplySortingAndPaging(pageRequest, SortableStaffFields, row => row.Staff.Id).ToListAsync(cancellationToken);
        return new PagedResult<StaffMemberListItemResponse>(page.Select(ToListItem).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalItemCount);
    }

    public async Task<IReadOnlyList<StaffMemberLookupItemResponse>> LookupAsync(string? searchText, int maximumItemCount, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var staff = databaseContext.StaffMembers.AsNoTracking()
            .Where(member => member.CompanyId == companyId && !member.IsDeleted && member.Status == ContactStatusCodes.Active);
        if (searchText is not null)
        {
            var pattern = LikePattern.Contains(searchText);
            staff = staff.Where(member => EF.Functions.ILike(member.Name, pattern, LikePattern.EscapeCharacter)
                                          || EF.Functions.ILike(member.Code, pattern, LikePattern.EscapeCharacter)
                                          || (member.Phone != null && EF.Functions.ILike(member.Phone, pattern, LikePattern.EscapeCharacter)));
        }

        return await staff.OrderBy(member => member.Name).ThenBy(member => member.Id).Take(maximumItemCount)
            .Select(member => new StaffMemberLookupItemResponse(member.Id, member.Code, member.Name, member.Phone, member.CurrentStoreId))
            .ToListAsync(cancellationToken);
    }

    public async Task<StaffMemberDetailResponse?> FindDetailAsync(Guid staffId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var row = await (await RowsAsync(cancellationToken)).Where(candidate => candidate.Staff.Id == staffId).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var openingBalance = await databaseContext.Database.SqlQuery<OpeningBalanceRow>($"""
            select entry_date as "AsOfDate", debit as "DebitAmount", credit as "CreditAmount"
              from acct.v_live_opening_balances
             where company_id = {companyId} and staff_id = {staffId}
            """).ToListAsync(cancellationToken);
        var opening = openingBalance.FirstOrDefault();

        var staff = row.Staff;
        return new StaffMemberDetailResponse(
            staff.Id, staff.Code, staff.Name, staff.Phone, staff.Email, staff.BirthDate, staff.CityId, row.CityName,
            staff.Designation, staff.IsManager, staff.CurrentStoreId, row.StoreName, staff.JoinedOn, staff.LeftOn, staff.Status,
            staff.CreatedAt, row.BalanceAmount,
            opening is null ? null : new ContactOpeningBalanceResponse(
                opening.DebitAmount > 0 ? OpeningBalanceSideCodes.Debit : OpeningBalanceSideCodes.Credit,
                opening.DebitAmount > 0 ? opening.DebitAmount : opening.CreditAmount, opening.AsOfDate),
            staff.RowVersion);
    }

    public async Task<Guid> AddAsync(StaffMemberWriteRecord record, Guid storeId, string code, Guid createdBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow.UtcDateTime);
        var staff = new StaffMember
        {
            CompanyId = currentUser.CompanyId,
            Code = code,
            CurrentStoreId = storeId,
            JoinedOn = today,
            CreatedBy = createdBy,
        };
        ApplyFields(staff, record);
        databaseContext.StaffMembers.Add(staff);
        await databaseContext.SaveChangesAsync(cancellationToken);   // the database generates the id; the history row needs it

        databaseContext.StaffStoreHistories.Add(new StaffStoreHistory
        {
            CompanyId = currentUser.CompanyId,
            StaffId = staff.Id,
            FromStoreId = null,
            ToStoreId = storeId,
            EffectiveOn = today,
            ChangedBy = createdBy,
        });
        await databaseContext.SaveChangesAsync(cancellationToken);
        return staff.Id;
    }

    public async Task UpdateAsync(Guid staffId, StaffMemberWriteRecord record, long rowVersion, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var staff = await databaseContext.StaffMembers.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == staffId && !candidate.IsDeleted, cancellationToken)
                    ?? throw new DomainException(ErrorKind.NotFound, "The staff member was not found.");
        // The store of an existing staff member only changes through a transfer, which leaves a history row.
        ApplyFields(staff, record);
        databaseContext.Entry(staff).Property(entity => entity.RowVersion).OriginalValue = rowVersion;
    }

    public async Task SoftDeleteAsync(Guid staffId, Guid deletedBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var staff = await databaseContext.StaffMembers.SingleOrDefaultAsync(candidate => candidate.CompanyId == companyId && candidate.Id == staffId && !candidate.IsDeleted, cancellationToken)
                    ?? throw new DomainException(ErrorKind.NotFound, "The staff member was not found.");
        staff.IsDeleted = true;
        staff.DeletedAt = dateTimeProvider.UtcNow;
        staff.DeletedBy = deletedBy;
    }

    public async Task<Guid?> FindStoreIdAsync(Guid staffId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.StaffMembers.AsNoTracking()
            .Where(member => member.CompanyId == companyId && member.Id == staffId && !member.IsDeleted)
            .Select(member => (Guid?)member.CurrentStoreId).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task TransferAsync(Guid staffId, Guid fromStoreId, Guid toStoreId, DateOnly effectiveOn, Guid changedBy, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var staff = await databaseContext.StaffMembers.SingleAsync(member => member.CompanyId == companyId && member.Id == staffId, cancellationToken);
        staff.CurrentStoreId = toStoreId;
        databaseContext.StaffStoreHistories.Add(new StaffStoreHistory
        {
            CompanyId = companyId,
            StaffId = staffId,
            FromStoreId = fromStoreId,
            ToStoreId = toStoreId,
            EffectiveOn = effectiveOn,
            ChangedBy = changedBy,
        });
    }

    public async Task<IReadOnlyList<StaffStoreHistoryResponse>> ListStoreHistoryAsync(Guid staffId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await (from history in databaseContext.StaffStoreHistories.AsNoTracking()
                          where history.CompanyId == companyId && history.StaffId == staffId
                          join toStore in databaseContext.Stores.AsNoTracking() on new { history.CompanyId, Id = history.ToStoreId } equals new { toStore.CompanyId, toStore.Id }
                          join fromStore in databaseContext.Stores.AsNoTracking() on new { history.CompanyId, Id = history.FromStoreId } equals new { fromStore.CompanyId, Id = (Guid?)fromStore.Id } into fromGroup
                          from fromStore in fromGroup.DefaultIfEmpty()
                          orderby history.EffectiveOn descending, history.ChangedAt descending
                          select new StaffHistoryRow { History = history, ToStoreName = toStore.Name, FromStoreName = fromStore == null ? null : fromStore.Name })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new StaffStoreHistoryResponse(row.History.Id, row.History.FromStoreId, row.FromStoreName, row.History.ToStoreId,
                                                                row.ToStoreName, row.History.EffectiveOn, row.History.ChangedBy, row.History.ChangedAt)).ToList();
    }

    // ---------------------------------------------------------------- helpers

    private static void ApplyFields(StaffMember staff, StaffMemberWriteRecord record)
    {
        staff.Name = record.Name;
        staff.Phone = record.Phone;
        staff.Email = record.Email;
        staff.BirthDate = record.BirthDate;
        staff.CityId = record.CityId;
        staff.Designation = record.Designation;
        staff.IsManager = record.IsManager;
        staff.Status = record.StatusCode;
    }

    private async Task<IQueryable<StaffRow>> RowsAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var cities = databaseContext.Set<MasterListItem>(MasterListDefinitions.City.EntityName).AsNoTracking();
        return from staff in databaseContext.StaffMembers.AsNoTracking()
               where staff.CompanyId == companyId && !staff.IsDeleted
               join city in cities on new { staff.CompanyId, Id = staff.CityId } equals new { city.CompanyId, Id = (Guid?)city.Id } into cityGroup
               from city in cityGroup.DefaultIfEmpty()
               join store in databaseContext.Stores.AsNoTracking() on new { staff.CompanyId, Id = staff.CurrentStoreId } equals new { store.CompanyId, store.Id } into storeGroup
               from store in storeGroup.DefaultIfEmpty()
               join balance in databaseContext.StaffBalances.AsNoTracking() on new { staff.CompanyId, StaffId = staff.Id } equals new { balance.CompanyId, balance.StaffId } into balanceGroup
               from balance in balanceGroup.DefaultIfEmpty()
               select new StaffRow
               {
                   Staff = staff,
                   CityName = city == null ? null : city.Name,
                   StoreName = store == null ? null : store.Name,
                   BalanceAmount = balance == null ? 0 : balance.Balance,
               };
    }

    private static System.Linq.Expressions.Expression<Func<StaffRow, bool>> MatchesSearchText(string searchText)
    {
        var pattern = LikePattern.Contains(searchText);
        return row => EF.Functions.ILike(row.Staff.Name, pattern, LikePattern.EscapeCharacter)
                      || EF.Functions.ILike(row.Staff.Code, pattern, LikePattern.EscapeCharacter)
                      || (row.Staff.Phone != null && EF.Functions.ILike(row.Staff.Phone, pattern, LikePattern.EscapeCharacter))
                      || (row.Staff.Email != null && EF.Functions.ILike(row.Staff.Email, pattern, LikePattern.EscapeCharacter))
                      || (row.CityName != null && EF.Functions.ILike(row.CityName, pattern, LikePattern.EscapeCharacter))
                      || (row.StoreName != null && EF.Functions.ILike(row.StoreName, pattern, LikePattern.EscapeCharacter))
                      || (row.Staff.Designation != null && EF.Functions.ILike(row.Staff.Designation, pattern, LikePattern.EscapeCharacter));
    }

    private static StaffMemberListItemResponse ToListItem(StaffRow row) => new(
        row.Staff.Id, row.Staff.Code, row.Staff.Name, row.Staff.Phone, row.Staff.Email, row.Staff.BirthDate, row.Staff.CityId, row.CityName,
        row.Staff.Designation, row.Staff.IsManager, row.Staff.CurrentStoreId, row.StoreName, row.Staff.Status, row.BalanceAmount, row.Staff.RowVersion);

    private sealed class StaffRow
    {
        public StaffMember Staff { get; set; } = null!;
        public string? CityName { get; set; }
        public string? StoreName { get; set; }
        public long BalanceAmount { get; set; }
    }

    private sealed class StaffHistoryRow
    {
        public StaffStoreHistory History { get; set; } = null!;
        public string ToStoreName { get; set; } = "";
        public string? FromStoreName { get; set; }
    }

    private sealed class OpeningBalanceRow
    {
        public DateOnly AsOfDate { get; set; }
        public long DebitAmount { get; set; }
        public long CreditAmount { get; set; }
    }
}
