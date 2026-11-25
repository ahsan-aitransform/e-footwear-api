using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Accounting.Repositories;

/// <summary>
/// The account reports. Figures come from the journal through the SQL report functions (rpt.account_ledger, rpt.trial_balance,
/// rpt.account_balance, rpt.general_journal), which check the REPORT_ACCOUNTS permission inside the database. The customer, vendor and staff balances of the
/// Account Balance report have no report function, so that query checks the same permission itself. Totals are separate aggregates and rows are
/// joined one to one, so amounts are never multiplied by a join.
/// </summary>
public sealed class AccountReportRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IAccountReportRepository
{
    private const string AccountPermissionCode = "REPORT_ACCOUNTS";

    // Shared by the page and the totals of the General Journal. Parameters: 0 company, 1 from, 2 to, 3 store, 4 type (voucher type or source).
    // A voucher entry carries its voucher type; the reversal entry of a voided voucher keeps its own source (VOUCHER_VOID) but shows the voucher number.
    private const string GeneralJournalSelection = """
        with journal as (
            select g.out_entry_id, g.out_entry_date, g.out_line_no, g.out_account_code, g.out_account_name, g.out_contact_name,
                   g.out_debit, g.out_credit, g.out_description, e.store_id, st.name as store_name,
                   case when g.out_source_type = 'VOUCHER' then v.voucher_type else g.out_source_type end as type_code, v.voucher_no
              from rpt.general_journal({0}, {1}, {2}, {3}) g
              join acct.journal_entries e on e.company_id = {0} and e.id = g.out_entry_id and e.entry_date = g.out_entry_date
              join core.stores st on st.company_id = {0} and st.id = e.store_id
              left join acct.vouchers v on v.company_id = {0} and v.id = g.out_source_id and g.out_source_type in ('VOUCHER', 'VOUCHER_VOID')
        )
        """;

    public async Task<IReadOnlyList<AccountLookupItemResponse>> SearchAccountsAsync(
        string? searchText, string? mainTypeCode, string? groupCode, int limit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var pattern = searchText is null ? null : LikePattern.Contains(searchText);

        var rows = await databaseContext.Database.SqlQuery<AccountRow>($"""
            select a.id as "AccountId", a.code as "Code", a.name as "Title", a.main_type as "MainTypeCode", a.group_code as "GroupCode",
                   coalesce(g.name, 'Group ' || a.group_code) as "GroupTitle", a.store_id as "StoreId", s.name as "StoreName"
              from acct.accounts a
              join core.stores s on s.company_id = a.company_id and s.id = a.store_id
              left join acct.account_groups g on g.company_id = a.company_id and g.group_code = a.group_code
             where a.company_id = {companyId}
               and ({mainTypeCode}::text is null or a.main_type = {mainTypeCode})
               and ({groupCode}::text is null or a.group_code = {groupCode})
               and ({pattern}::text is null or a.code ilike {pattern} escape '\' or a.name ilike {pattern} escape '\')
             order by a.code
             limit {limit}
            """).ToListAsync(cancellationToken);
        return rows.Select(ToLookupItem).ToList();
    }

    public async Task<AccountLookupItemResponse?> FindAccountAsync(Guid? accountId, string? accountCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<AccountRow>($"""
            select a.id as "AccountId", a.code as "Code", a.name as "Title", a.main_type as "MainTypeCode", a.group_code as "GroupCode",
                   coalesce(g.name, 'Group ' || a.group_code) as "GroupTitle", a.store_id as "StoreId", s.name as "StoreName"
              from acct.accounts a
              join core.stores s on s.company_id = a.company_id and s.id = a.store_id
              left join acct.account_groups g on g.company_id = a.company_id and g.group_code = a.group_code
             where a.company_id = {companyId}
               and (({accountId}::uuid is not null and a.id = {accountId}) or ({accountCode}::text is not null and a.code = {accountCode}))
             limit 1
            """).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : ToLookupItem(rows[0]);
    }

    public async Task<IReadOnlyList<AccountBalanceTypeOptionResponse>> ListBalanceTypeOptionsAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var groups = await databaseContext.AccountGroups.AsNoTracking()
            .Where(group => group.CompanyId == companyId).OrderBy(group => group.GroupCode)
            .Select(group => new { group.GroupCode, group.Name }).ToListAsync(cancellationToken);

        var options = new List<AccountBalanceTypeOptionResponse>();
        options.AddRange(AccountMainTypes.All.Select(mainType =>
            new AccountBalanceTypeOptionResponse(AccountReportRules.MainTypeFilterPrefix + mainType.Code, mainType.DisplayName)));
        options.AddRange(groups.Select(group =>
            new AccountBalanceTypeOptionResponse(AccountReportRules.GroupFilterPrefix + group.GroupCode, $"{group.GroupCode} {group.Name}")));
        options.Add(new AccountBalanceTypeOptionResponse(AccountReportRules.ContactsFilterPrefix + ContactKindCodes.Customer, "Customers"));
        options.Add(new AccountBalanceTypeOptionResponse(AccountReportRules.ContactsFilterPrefix + ContactKindCodes.Vendor, "Vendors"));
        options.Add(new AccountBalanceTypeOptionResponse(AccountReportRules.ContactsFilterPrefix + ContactKindCodes.Staff, "Staff"));
        return options;
    }

    public async Task<AccountLedgerResponse> GetLedgerAsync(
        AccountLookupItemResponse account, DateOnly fromDate, DateOnly toDate, Guid? storeId, bool includeDescriptions,
        PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        var openingBalance = await databaseContext.Database.SqlQuery<long>($"""
            select cast(coalesce(sum(l.debit - l.credit), 0) as bigint) as "Value"
              from acct.journal_lines l
             where l.company_id = {companyId} and l.account_id = {account.AccountId} and l.entry_date < {fromDate}
               and (cast({storeId} as uuid) is null or l.store_id = cast({storeId} as uuid))
            """).SingleAsync(cancellationToken);

        var totals = await databaseContext.Database.SqlQuery<LedgerTotalsRow>($"""
            select cast(coalesce(sum(t.out_debit), 0) as bigint) as "TotalDebitAmount",
                   cast(coalesce(sum(t.out_credit), 0) as bigint) as "TotalCreditAmount",
                   count(*) as "LineCount"
              from rpt.account_ledger({companyId}, {account.AccountId}, {fromDate}, {toDate}, {storeId}) t
            """).SingleAsync(cancellationToken);

        var skippedLineCount = pageRequest.SkippedItemCount;
        var pageSize = pageRequest.PageSize;
        var rows = await databaseContext.Database.SqlQuery<LedgerRow>($"""
            select t.out_entry_date as "EntryDate", t.out_entry_id as "EntryId", t.out_source_type as "SourceTypeCode",
                   t.out_description as "Description", t.out_contact_kind as "ContactKindCode", t.out_contact_id as "ContactId", t.out_debit as "DebitAmount",
                   t.out_credit as "CreditAmount", t.out_running as "RunningBalance"
              from rpt.account_ledger({companyId}, {account.AccountId}, {fromDate}, {toDate}, {storeId})
                   with ordinality as t(out_entry_date, out_entry_id, out_source_type, out_description, out_contact_kind, out_contact_id,
                                        out_debit, out_credit, out_running, line_order)
             order by t.line_order
             offset {skippedLineCount} limit {pageSize}
            """).ToListAsync(cancellationToken);

        var closingBalance = openingBalance + totals.TotalDebitAmount - totals.TotalCreditAmount;
        var lines = new PagedResult<AccountLedgerRowResponse>(
            rows.Select(row => new AccountLedgerRowResponse(
                row.EntryDate, row.EntryId, row.SourceTypeCode, includeDescriptions ? row.Description : null, row.ContactKindCode,
                row.ContactKindCode == ContactKindCodes.Customer ? row.ContactId : null,
                row.ContactKindCode == ContactKindCodes.Vendor ? row.ContactId : null,
                row.ContactKindCode == ContactKindCodes.Staff ? row.ContactId : null,
                row.DebitAmount, row.CreditAmount, Math.Abs(row.RunningBalance), BalanceSideCodes.SideOf(row.RunningBalance))).ToList(),
            pageRequest.PageNumber, pageRequest.PageSize, totals.LineCount);
        return new AccountLedgerResponse(
            account, fromDate, toDate, storeId,
            Math.Abs(openingBalance), BalanceSideCodes.SideOf(openingBalance),
            totals.TotalDebitAmount, totals.TotalCreditAmount,
            Math.Abs(closingBalance), BalanceSideCodes.SideOf(closingBalance), lines);
    }

    public async Task<AccountBalanceReportResponse> GetAccountBalancesAsync(
        DateOnly asOfDate, Guid? storeId, string accountTypeFilter, string amountTypeFilter, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        List<BalanceRow> balances;
        string? contactKindCode = null;
        if (accountTypeFilter.StartsWith(AccountReportRules.ContactsFilterPrefix, StringComparison.Ordinal))
        {
            contactKindCode = accountTypeFilter[AccountReportRules.ContactsFilterPrefix.Length..];
            var userId = currentUser.UserId;
            // No report function exists for customer, vendor and staff balances as on a date: apply the same permission check the other account reports get from the database.
            await databaseContext.Database.ExecuteSqlAsync($"select sec.require_permission({companyId}, {userId}, {AccountPermissionCode})", cancellationToken);
            // The table and the column are chosen from fixed names (never from user input), so they are safe to put into the SQL text.
            var (contactTable, ownerColumn, typeTitle) = contactKindCode switch
            {
                ContactKindCodes.Customer => ("contacts.customers", "customer_id", "Customer"),
                ContactKindCodes.Vendor => ("contacts.vendors", "vendor_id", "Vendor"),
                _ => ("contacts.staff", "staff_id", "Staff"),
            };
            balances = await databaseContext.Database.SqlQueryRaw<BalanceRow>("""
                select cast(null as uuid) as "AccountId", p.id as "ContactId", p.code as "Code", p.name as "Title",
                       '__TYPE_TITLE__' as "TypeTitle",
                       cast(sum(l.debit - l.credit) as bigint) as "Balance"
                  from __CONTACT_TABLE__ p
                  join acct.journal_lines l on l.company_id = p.company_id and l.__OWNER_COLUMN__ = p.id and l.entry_date <= cast({1} as date)
                 where p.company_id = {0} and not p.is_deleted
                   and (cast({2} as uuid) is null or l.store_id = cast({2} as uuid))
                 group by p.id, p.code, p.name
                having sum(l.debit - l.credit) <> 0
                 order by p.name
                """.Replace("__TYPE_TITLE__", typeTitle).Replace("__CONTACT_TABLE__", contactTable).Replace("__OWNER_COLUMN__", ownerColumn),
                companyId, asOfDate, (object?)storeId ?? DBNull.Value).ToListAsync(cancellationToken);
        }
        else
        {
            var mainTypeCode = accountTypeFilter.StartsWith(AccountReportRules.MainTypeFilterPrefix, StringComparison.Ordinal)
                ? accountTypeFilter[AccountReportRules.MainTypeFilterPrefix.Length..] : null;
            var groupCode = accountTypeFilter.StartsWith(AccountReportRules.GroupFilterPrefix, StringComparison.Ordinal)
                ? accountTypeFilter[AccountReportRules.GroupFilterPrefix.Length..] : null;
            balances = await databaseContext.Database.SqlQuery<BalanceRow>($"""
                select b.out_account_id as "AccountId", cast(null as uuid) as "ContactId", b.out_code as "Code", b.out_name as "Title",
                       coalesce(g.name, 'Group ' || a.group_code) as "TypeTitle", cast(b.out_balance as bigint) as "Balance"
                  from rpt.account_balance({companyId}, {asOfDate}, {storeId}) b
                  join acct.accounts a on a.company_id = {companyId} and a.id = b.out_account_id
                  left join acct.account_groups g on g.company_id = a.company_id and g.group_code = a.group_code
                 where b.out_balance <> 0
                   and ({mainTypeCode}::text is null or a.main_type = {mainTypeCode})
                   and ({groupCode}::text is null or a.group_code = {groupCode})
                 order by b.out_code
                """).ToListAsync(cancellationToken);
        }

        var shown = balances.Where(row => amountTypeFilter switch
        {
            BalanceSideCodes.Debit => row.Balance > 0,
            BalanceSideCodes.Credit => row.Balance < 0,
            _ => true,
        }).ToList();
        var rows = shown.Select(row => new AccountBalanceRowResponse(
            row.AccountId, contactKindCode == ContactKindCodes.Customer ? row.ContactId : null, contactKindCode == ContactKindCodes.Vendor ? row.ContactId : null,
            contactKindCode == ContactKindCodes.Staff ? row.ContactId : null, row.Code, row.Title, row.TypeTitle, Math.Max(row.Balance, 0), Math.Max(-row.Balance, 0))).ToList();
        return new AccountBalanceReportResponse(asOfDate, storeId, accountTypeFilter, amountTypeFilter, rows,
                                                rows.Sum(row => row.DebitAmount), rows.Sum(row => row.CreditAmount));
    }

    public async Task<TrialBalanceResponse> GetTrialBalanceAsync(
        DateOnly asOfDate, Guid? storeId, bool isDetailed, bool includeZeroBalances, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var fromDate = AccountReportRules.EarliestReportDate;

        // Period totals from the very beginning to the date: the function's opening balance is empty and its period debit/credit are the totals.
        var accountRows = await databaseContext.Database.SqlQuery<TrialBalanceAccountRow>($"""
            select t.out_account_id as "AccountId", t.out_code as "Code", t.out_name as "Title", t.out_main_type as "MainTypeCode",
                   a.group_code as "GroupCode", coalesce(g.name, 'Group ' || a.group_code) as "GroupTitle",
                   cast(t.out_period_debit as bigint) as "TotalDebit", cast(t.out_period_credit as bigint) as "TotalCredit"
              from rpt.trial_balance({companyId}, {fromDate}, {asOfDate}, {storeId}) t
              join acct.accounts a on a.company_id = {companyId} and a.id = t.out_account_id
              left join acct.account_groups g on g.company_id = a.company_id and g.group_code = a.group_code
             order by a.group_code, t.out_code
            """).ToListAsync(cancellationToken);

        var shownAccounts = accountRows.Where(row => includeZeroBalances || row.TotalDebit + row.TotalCredit != 0).ToList();
        var rows = new List<TrialBalanceRowResponse>();
        foreach (var group in shownAccounts.GroupBy(row => (row.GroupCode, row.GroupTitle, row.MainTypeCode)))
        {
            var groupDebit = group.Sum(row => row.TotalDebit);
            var groupCredit = group.Sum(row => row.TotalCredit);
            rows.Add(ToTrialBalanceRow(TrialBalanceRowKindCodes.Group, group.Key.GroupCode, group.Key.GroupTitle, null, null, null, group.Key.MainTypeCode, groupDebit, groupCredit));
            if (!isDetailed) continue;
            rows.AddRange(group.Select(row => ToTrialBalanceRow(
                TrialBalanceRowKindCodes.Account, row.GroupCode, row.GroupTitle, row.AccountId, row.Code, row.Title, row.MainTypeCode, row.TotalDebit, row.TotalCredit)));
        }

        // Totals add the GROUP rows only: an account row repeats what its group row already counted.
        var groupRows = rows.Where(row => row.RowKindCode == TrialBalanceRowKindCodes.Group).ToList();
        var totalDebit = groupRows.Sum(row => row.TotalDebitAmount);
        var totalCredit = groupRows.Sum(row => row.TotalCreditAmount);
        return new TrialBalanceResponse(asOfDate, storeId, isDetailed, rows, totalDebit, totalCredit,
                                        groupRows.Sum(row => row.BalanceDebitAmount), groupRows.Sum(row => row.BalanceCreditAmount),
                                        totalDebit == totalCredit);
    }

    public async Task<GeneralJournalResponse> GetGeneralJournalAsync(
        DateOnly fromDate, DateOnly toDate, Guid? storeId, string? typeCode, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var selectionParameters = new object?[] { companyId, fromDate, toDate, storeId, typeCode };
        const string typeFilter = "where (cast({4} as text) is null or journal.type_code = cast({4} as text))";

        var totals = await databaseContext.Database.SqlQueryRaw<JournalTotalsRow>($"""
            {GeneralJournalSelection}
            select cast(coalesce(sum(journal.out_debit), 0) as bigint) as "TotalDebitAmount",
                   cast(coalesce(sum(journal.out_credit), 0) as bigint) as "TotalCreditAmount",
                   count(*) as "LineCount"
              from journal {typeFilter}
            """, selectionParameters).SingleAsync(cancellationToken);

        var rows = await databaseContext.Database.SqlQueryRaw<JournalRow>($$"""
            {{GeneralJournalSelection}}
            select journal.out_entry_id as "EntryId", journal.out_entry_date as "EntryDate", journal.out_line_no as "LineNumber",
                   journal.store_id as "StoreId", journal.store_name as "StoreName", journal.out_account_code as "AccountCode",
                   journal.out_account_name as "AccountTitle", journal.out_contact_name as "ContactName", journal.type_code as "TypeCode",
                   journal.voucher_no as "VoucherNumber", journal.out_description as "Description",
                   journal.out_debit as "DebitAmount", journal.out_credit as "CreditAmount"
              from journal {{typeFilter}}
             order by journal.out_entry_date, journal.out_entry_id, journal.out_line_no
             offset {5} limit {6}
            """, [.. selectionParameters, pageRequest.SkippedItemCount, pageRequest.PageSize]).ToListAsync(cancellationToken);

        var lines = new PagedResult<GeneralJournalRowResponse>(
            rows.Select(row => new GeneralJournalRowResponse(
                row.EntryId, row.EntryDate, row.LineNumber, row.StoreId, row.StoreName, row.AccountCode, row.AccountTitle, row.ContactName,
                row.TypeCode, row.VoucherNumber, row.Description, row.DebitAmount, row.CreditAmount)).ToList(),
            pageRequest.PageNumber, pageRequest.PageSize, totals.LineCount);
        return new GeneralJournalResponse(fromDate, toDate, storeId, typeCode, totals.TotalDebitAmount, totals.TotalCreditAmount, lines);
    }

    private static AccountLookupItemResponse ToLookupItem(AccountRow row) =>
        new(row.AccountId, row.Code, row.Title, row.MainTypeCode, row.GroupCode, row.GroupTitle, row.StoreId, row.StoreName);

    private static TrialBalanceRowResponse ToTrialBalanceRow(
        string rowKindCode, string groupCode, string groupTitle, Guid? accountId, string? accountCode, string? accountTitle,
        string mainTypeCode, long totalDebit, long totalCredit)
    {
        var netBalance = totalDebit - totalCredit;
        return new TrialBalanceRowResponse(rowKindCode, groupCode, groupTitle, accountId, accountCode, accountTitle, mainTypeCode,
                                           totalDebit, totalCredit, Math.Max(netBalance, 0), Math.Max(-netBalance, 0));
    }

    private sealed class AccountRow
    {
        public Guid AccountId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string MainTypeCode { get; set; } = "";
        public string GroupCode { get; set; } = "";
        public string GroupTitle { get; set; } = "";
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
    }

    private sealed class LedgerTotalsRow
    {
        public long TotalDebitAmount { get; set; }
        public long TotalCreditAmount { get; set; }
        public long LineCount { get; set; }
    }

    private sealed class LedgerRow
    {
        public DateOnly EntryDate { get; set; }
        public Guid EntryId { get; set; }
        public string SourceTypeCode { get; set; } = "";
        public string? Description { get; set; }
        public string? ContactKindCode { get; set; }
        public Guid? ContactId { get; set; }
        public long DebitAmount { get; set; }
        public long CreditAmount { get; set; }
        public long RunningBalance { get; set; }
    }

    private sealed class BalanceRow
    {
        public Guid? AccountId { get; set; }
        public Guid? ContactId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string TypeTitle { get; set; } = "";
        public long Balance { get; set; }
    }

    private sealed class TrialBalanceAccountRow
    {
        public Guid AccountId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string MainTypeCode { get; set; } = "";
        public string GroupCode { get; set; } = "";
        public string GroupTitle { get; set; } = "";
        public long TotalDebit { get; set; }
        public long TotalCredit { get; set; }
    }

    private sealed class JournalTotalsRow
    {
        public long TotalDebitAmount { get; set; }
        public long TotalCreditAmount { get; set; }
        public long LineCount { get; set; }
    }

    private sealed class JournalRow
    {
        public Guid EntryId { get; set; }
        public DateOnly EntryDate { get; set; }
        public int LineNumber { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public string AccountCode { get; set; } = "";
        public string AccountTitle { get; set; } = "";
        public string? ContactName { get; set; }
        public string TypeCode { get; set; } = "";
        public long? VoucherNumber { get; set; }
        public string? Description { get; set; }
        public long DebitAmount { get; set; }
        public long CreditAmount { get; set; }
    }
}
