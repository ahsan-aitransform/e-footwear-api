using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.HumanResources.Abstractions;
using EFootwear.BLL.HumanResources.Contracts;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.HumanResources.Repositories;

/// <summary>
/// Staff profiles, the salary history and the store history of staff. Reads are plain SQL over contacts.staff, hr.salary_terms and
/// contacts.staff_store_history; the staff tables are isolated by company only, so every query also limits itself to the stores the caller may use.
/// The only write is the salary terms: the database adds the effective-dated history row by trigger.
/// </summary>
public sealed class StaffRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IStaffRepository
{
    private static readonly IReadOnlyDictionary<string, string> StaffSortableColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "p.name",
        ["code"] = "p.code",
        ["store"] = "s.name",
        ["designation"] = "sp.designation",
        ["salary"] = "sp.base_salary",
    };

    private static readonly IReadOnlyDictionary<string, string> TransferSortableColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["date"] = "h.effective_on",
        ["name"] = "p.name",
        ["code"] = "p.code",
    };

    // Parameters: 0 company, 1 store, 2 include former staff, 3 search pattern, 4 may use every store, 5 the stores the caller may use.
    private const string StaffFromAndWhere = """
        from contacts.staff sp
        join contacts.staff p on p.company_id = sp.company_id and p.id = sp.id
        join core.stores s on s.company_id = sp.company_id and s.id = sp.current_store_id
        where sp.company_id = {0} and not p.is_deleted
          and (cast({1} as uuid) is null or sp.current_store_id = cast({1} as uuid))
          and (cast({2} as boolean) or sp.left_on is null or sp.left_on >= current_date)
          and (cast({3} as text) is null
               or p.name ilike cast({3} as text) escape '\'
               or p.code ilike cast({3} as text) escape '\'
               or sp.designation ilike cast({3} as text) escape '\')
          and (cast({4} as boolean) or sp.current_store_id = any(cast({5} as uuid[])))
        """;

    private const string StaffColumns = """
        sp.id as "StaffId", p.code as "Code", p.name as "Name", sp.designation as "Designation", sp.is_manager as "IsManager",
        sp.current_store_id as "CurrentStoreId", s.name as "CurrentStoreName", sp.base_salary as "BaseSalary",
        sp.pay_frequency as "PayFrequencyCode", sp.joined_on as "JoinedOn", sp.left_on as "LeftOn", p.status as "StatusCode"
        """;

    // Parameters: 0 company, 1 staff member, 2 store, 3 first date, 4 last date, 5 search pattern, 6 may use every store, 7 the stores the caller may use.
    private const string TransferFromAndWhere = """
        from contacts.staff_store_history h
        join contacts.staff p on p.company_id = h.company_id and p.id = h.staff_id
        join core.stores to_store on to_store.company_id = h.company_id and to_store.id = h.to_store_id
        left join core.stores from_store on from_store.company_id = h.company_id and from_store.id = h.from_store_id
        left join sec.users changed_by_user on changed_by_user.company_id = h.company_id and changed_by_user.id = h.changed_by
        where h.company_id = {0}
          and (cast({1} as uuid) is null or h.staff_id = cast({1} as uuid))
          and (cast({2} as uuid) is null or h.to_store_id = cast({2} as uuid) or h.from_store_id = cast({2} as uuid))
          and (cast({3} as date) is null or h.effective_on >= cast({3} as date))
          and (cast({4} as date) is null or h.effective_on <= cast({4} as date))
          and (cast({5} as text) is null or p.name ilike cast({5} as text) escape '\' or p.code ilike cast({5} as text) escape '\')
          and (cast({6} as boolean) or h.to_store_id = any(cast({7} as uuid[])) or h.from_store_id = any(cast({7} as uuid[])))
        """;

    public async Task<PagedResult<StaffListItemResponse>> SearchStaffAsync(StaffSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        object?[] filterParameters =
        [
            currentUser.CompanyId, filter.StoreId, filter.IncludeFormerStaff,
            pageRequest.SearchText is null ? null : LikePattern.Contains(pageRequest.SearchText),
            currentUser.HasAccessToAllStores, currentUser.AssignedStoreIds.ToArray(),
        ];

        var totalCount = await databaseContext.Database
            .SqlQueryRaw<long>($"select count(*) as \"Value\" {StaffFromAndWhere}", filterParameters)
            .SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<StaffListItemResponse>.Empty(pageRequest);

        var direction = pageRequest.SortDirection == SortDirection.Descending ? "desc" : "asc";
        var primaryOrder = pageRequest.SortField is { } sortField && StaffSortableColumns.TryGetValue(sortField, out var column)
            ? $"{column} {direction}, " : "";
        var parameters = filterParameters.Concat(new object?[] { pageRequest.PageSize, pageRequest.SkippedItemCount }).ToArray();

        var rows = await databaseContext.Database.SqlQueryRaw<StaffRow>($$"""
            select {{StaffColumns}} {{StaffFromAndWhere}}
             order by {{primaryOrder}}p.name, sp.id
             limit {6} offset {7}
            """, parameters).ToListAsync(cancellationToken);
        return new PagedResult<StaffListItemResponse>(rows.Select(ToStaffListItem).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    public async Task<StaffDetailResponse?> FindStaffDetailAsync(Guid staffId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        var profile = await databaseContext.Database.SqlQuery<StaffDetailRow>($"""
            select sp.id as "StaffId", p.code as "Code", p.name as "Name", sp.designation as "Designation", sp.is_manager as "IsManager",
                   sp.current_store_id as "CurrentStoreId", s.name as "CurrentStoreName", sp.base_salary as "BaseSalary",
                   sp.pay_frequency as "PayFrequencyCode", sp.joined_on as "JoinedOn", sp.left_on as "LeftOn", p.status as "StatusCode",
                   sp.row_ver as "RowVersion"
              from contacts.staff sp
              join contacts.staff p on p.company_id = sp.company_id and p.id = sp.id
              join core.stores s on s.company_id = sp.company_id and s.id = sp.current_store_id
             where sp.company_id = {companyId} and sp.id = {staffId} and not p.is_deleted
            """).SingleOrDefaultAsync(cancellationToken);
        if (profile is null) return null;

        var salaryTerms = await databaseContext.Database.SqlQuery<SalaryTermRow>($"""
            select t.id as "Id", t.effective_from as "EffectiveFrom", t.base_salary as "BaseSalary", t.pay_frequency as "PayFrequencyCode",
                   t.changed_by as "ChangedByUserId", u.full_name as "ChangedByName", t.changed_at as "ChangedAt"
              from hr.salary_terms t
              left join sec.users u on u.company_id = t.company_id and u.id = t.changed_by
             where t.company_id = {companyId} and t.staff_id = {staffId}
             order by t.effective_from desc, t.changed_at desc
            """).ToListAsync(cancellationToken);

        var transfers = await databaseContext.Database.SqlQuery<TransferHistoryRow>($"""
            select h.id as "Id", h.from_store_id as "FromStoreId", from_store.name as "FromStoreName", h.to_store_id as "ToStoreId",
                   to_store.name as "ToStoreName", h.effective_on as "EffectiveOn", h.changed_by as "ChangedByUserId",
                   changed_by_user.full_name as "ChangedByName", h.changed_at as "ChangedAt"
              from contacts.staff_store_history h
              join core.stores to_store on to_store.company_id = h.company_id and to_store.id = h.to_store_id
              left join core.stores from_store on from_store.company_id = h.company_id and from_store.id = h.from_store_id
              left join sec.users changed_by_user on changed_by_user.company_id = h.company_id and changed_by_user.id = h.changed_by
             where h.company_id = {companyId} and h.staff_id = {staffId}
             order by h.effective_on desc, h.changed_at desc
            """).ToListAsync(cancellationToken);

        return new StaffDetailResponse(
            ToStaffListItem(profile),
            profile.RowVersion,
            salaryTerms.Select(row => new SalaryTermResponse(row.Id, row.EffectiveFrom, row.BaseSalary, row.PayFrequencyCode,
                                                             row.ChangedByUserId, row.ChangedByName, row.ChangedAt)).ToList(),
            transfers.Select(row => new StaffTransferHistoryItemResponse(row.Id, row.FromStoreId, row.FromStoreName, row.ToStoreId, row.ToStoreName,
                                                                         row.EffectiveOn, row.ChangedByUserId, row.ChangedByName, row.ChangedAt)).ToList());
    }

    public async Task<bool> UpdateSalaryTermsAsync(Guid staffId, long baseSalary, string payFrequencyCode, long expectedRowVersion, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var userId = currentUser.UserId;
        // The trigger hr.log_salary_term adds the effective-dated salary history row; the row version makes a stale save fail instead of overwriting.
        var changedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update contacts.staff
               set base_salary = {baseSalary}, pay_frequency = {payFrequencyCode}, updated_by = {userId}
             where company_id = {companyId} and id = {staffId} and row_ver = {expectedRowVersion}
            """, cancellationToken);
        return changedRows == 1;
    }

    public async Task<PagedResult<StaffTransferListItemResponse>> SearchTransfersAsync(
        StaffTransferSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        object?[] filterParameters =
        [
            currentUser.CompanyId, filter.StaffId, filter.StoreId, filter.FromDate, filter.ToDate,
            pageRequest.SearchText is null ? null : LikePattern.Contains(pageRequest.SearchText),
            currentUser.HasAccessToAllStores, currentUser.AssignedStoreIds.ToArray(),
        ];

        var totalCount = await databaseContext.Database
            .SqlQueryRaw<long>($"select count(*) as \"Value\" {TransferFromAndWhere}", filterParameters)
            .SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<StaffTransferListItemResponse>.Empty(pageRequest);

        var direction = pageRequest.SortDirection == SortDirection.Descending ? "desc" : "asc";
        var primaryOrder = pageRequest.SortField is { } sortField && TransferSortableColumns.TryGetValue(sortField, out var column)
            ? $"{column} {direction}, " : "";
        var parameters = filterParameters.Concat(new object?[] { pageRequest.PageSize, pageRequest.SkippedItemCount }).ToArray();

        var rows = await databaseContext.Database.SqlQueryRaw<TransferListRow>($$"""
            select h.id as "Id", h.staff_id as "StaffId", p.code as "StaffCode", p.name as "StaffName",
                   h.from_store_id as "FromStoreId", from_store.name as "FromStoreName", h.to_store_id as "ToStoreId", to_store.name as "ToStoreName",
                   h.effective_on as "EffectiveOn", h.changed_by as "ChangedByUserId", changed_by_user.full_name as "ChangedByName",
                   h.changed_at as "ChangedAt"
            {{TransferFromAndWhere}}
             order by {{primaryOrder}}h.effective_on desc, h.changed_at desc, h.id
             limit {8} offset {9}
            """, parameters).ToListAsync(cancellationToken);
        return new PagedResult<StaffTransferListItemResponse>(
            rows.Select(row => new StaffTransferListItemResponse(row.Id, row.StaffId, row.StaffCode, row.StaffName, row.FromStoreId, row.FromStoreName,
                                                                 row.ToStoreId, row.ToStoreName, row.EffectiveOn, row.ChangedByUserId,
                                                                 row.ChangedByName, row.ChangedAt)).ToList(),
            pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    private static StaffListItemResponse ToStaffListItem(StaffRow row) =>
        new(row.StaffId, row.Code, row.Name, row.Designation, row.IsManager, row.CurrentStoreId, row.CurrentStoreName, row.BaseSalary,
            row.PayFrequencyCode, row.JoinedOn, row.LeftOn, row.StatusCode);

    private class StaffRow
    {
        public Guid StaffId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Designation { get; set; }
        public bool IsManager { get; set; }
        public Guid CurrentStoreId { get; set; }
        public string CurrentStoreName { get; set; } = "";
        public long BaseSalary { get; set; }
        public string PayFrequencyCode { get; set; } = "";
        public DateOnly? JoinedOn { get; set; }
        public DateOnly? LeftOn { get; set; }
        public string StatusCode { get; set; } = "";
    }

    private sealed class StaffDetailRow : StaffRow
    {
        public long RowVersion { get; set; }
    }

    private sealed class SalaryTermRow
    {
        public Guid Id { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public long BaseSalary { get; set; }
        public string PayFrequencyCode { get; set; } = "";
        public Guid? ChangedByUserId { get; set; }
        public string? ChangedByName { get; set; }
        public DateTimeOffset ChangedAt { get; set; }
    }

    private sealed class TransferHistoryRow
    {
        public Guid Id { get; set; }
        public Guid? FromStoreId { get; set; }
        public string? FromStoreName { get; set; }
        public Guid ToStoreId { get; set; }
        public string ToStoreName { get; set; } = "";
        public DateOnly EffectiveOn { get; set; }
        public Guid? ChangedByUserId { get; set; }
        public string? ChangedByName { get; set; }
        public DateTimeOffset ChangedAt { get; set; }
    }

    private sealed class TransferListRow
    {
        public Guid Id { get; set; }
        public Guid StaffId { get; set; }
        public string StaffCode { get; set; } = "";
        public string StaffName { get; set; } = "";
        public Guid? FromStoreId { get; set; }
        public string? FromStoreName { get; set; }
        public Guid ToStoreId { get; set; }
        public string ToStoreName { get; set; } = "";
        public DateOnly EffectiveOn { get; set; }
        public Guid? ChangedByUserId { get; set; }
        public string? ChangedByName { get; set; }
        public DateTimeOffset ChangedAt { get; set; }
    }
}
