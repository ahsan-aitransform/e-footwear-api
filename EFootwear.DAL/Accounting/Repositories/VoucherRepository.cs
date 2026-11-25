using System.Text.Json;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Common;
using EFootwear.DAL.Common;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Accounting.Repositories;

/// <summary>
/// Vouchers. Writes go through acct.post_voucher and acct.void_voucher (migration 0700), which post the journal entry and the voucher together
/// and check the VOUCHER_POST / VOUCHER_DELETE permissions again inside the database. Reads are plain SQL over acct.vouchers, the journal and the
/// customers, vendors and staff; row-level security limits them to the caller's company and stores.
/// </summary>
public sealed class VoucherRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IVoucherRepository
{
    private static readonly IReadOnlyDictionary<string, string> SortableColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["date"] = "v.voucher_date",
        ["number"] = "v.voucher_no",
        ["amount"] = "v.amount",
        ["type"] = "v.voucher_type",
        ["store"] = "s.name",
        ["status"] = "v.status",
    };

    // Shared FROM and WHERE of the list, the count and the report. Parameters: 0 company, 1 type, 2 store, 3 from, 4 to, 5 status, 6 customer,
    // 7 vendor, 8 staff, 9 search text (exact voucher number), 10 search pattern. Every optional filter is cast so PostgreSQL can type a null.
    private const string VoucherFilterFromAndWhere = """
        from acct.vouchers v
        join core.stores s on s.company_id = v.company_id and s.id = v.store_id
        join acct.accounts debit_account on debit_account.company_id = v.company_id and debit_account.id = v.debit_account_id
        join acct.accounts credit_account on credit_account.company_id = v.company_id and credit_account.id = v.credit_account_id
        left join contacts.v_contact_directory p on p.company_id = v.company_id
               and p.kind_code = (case when v.customer_id is not null then 'CUSTOMER' when v.vendor_id is not null then 'VENDOR' else 'STAFF' end)
               and p.id = coalesce(v.customer_id, v.vendor_id, v.staff_id)
        where v.company_id = {0}
          and (cast({1} as text) is null or v.voucher_type = cast({1} as text))
          and (cast({2} as uuid) is null or v.store_id = cast({2} as uuid))
          and (cast({3} as date) is null or v.voucher_date >= cast({3} as date))
          and (cast({4} as date) is null or v.voucher_date <= cast({4} as date))
          and (cast({5} as text) is null or v.status = cast({5} as text))
          and (cast({6} as uuid) is null or v.customer_id = cast({6} as uuid))
          and (cast({7} as uuid) is null or v.vendor_id = cast({7} as uuid))
          and (cast({8} as uuid) is null or v.staff_id = cast({8} as uuid))
          and (cast({9} as text) is null
               or v.voucher_no::text = cast({9} as text)
               or v.ref_no ilike cast({10} as text) escape '\'
               or v.description ilike cast({10} as text) escape '\'
               or p.name ilike cast({10} as text) escape '\'
               or debit_account.name ilike cast({10} as text) escape '\'
               or credit_account.name ilike cast({10} as text) escape '\')
        """;

    private const string VoucherListColumns = """
        v.id as "Id", v.store_id as "StoreId", s.name as "StoreName", v.voucher_type as "VoucherTypeCode", v.voucher_no as "VoucherNumber",
        v.voucher_date as "VoucherDate", v.amount as "Amount", debit_account.name as "DebitAccountTitle", credit_account.name as "CreditAccountTitle",
        p.name as "ContactName", v.ref_no as "ReferenceNumber", v.description as "Description", v.status as "StatusCode"
        """;

    public async Task<long> GetNextVoucherNumberAsync(Guid storeId, string voucherTypeCode, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var seriesCode = voucherTypeCode + "_VOUCHER";
        return await databaseContext.Database.SqlQuery<long>($"""
            select coalesce((select next_value from core.number_series
                              where company_id = {companyId} and store_id = {storeId} and series_code = {seriesCode}
                                and period_key = date '1900-01-01'), 1) as "Value"
            """).SingleAsync(cancellationToken);
    }

    public async Task<Guid> PostAsync(string voucherTypeCode, PostVoucherRequest request, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var debitContact = request.DebitTarget.FindContact("To (debit)");
        var creditContact = request.CreditTarget.FindContact("From (credit)");
        var allocationsJson = request.Allocations is { Count: > 0 } allocations
            ? JsonSerializer.Serialize(allocations.Select(allocation => new { invoiceId = allocation.InvoiceId, amount = allocation.Amount }))
            : null;

        return await databaseContext.Database.SqlQuery<Guid>($"""
            select acct.post_voucher({companyId}, {request.StoreId}, {voucherTypeCode}, {request.VoucherDate},
                                     {request.DebitTarget.AccountId}, {debitContact?.KindCode}, {debitContact?.Id},
                                     {request.CreditTarget.AccountId}, {creditContact?.KindCode}, {creditContact?.Id},
                                     {request.Amount}, {request.PaymentMode}, {request.ReferenceNumber}, {request.Description},
                                     {request.ClientTransactionId}, cast({allocationsJson} as jsonb)) as "Value"
            """).SingleAsync(cancellationToken);
    }

    public async Task<VoucherResponse?> FindByIdAsync(Guid voucherId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        var header = await databaseContext.Database.SqlQuery<VoucherHeaderRow>($"""
            select v.id as "Id", v.store_id as "StoreId", s.name as "StoreName", v.voucher_type as "VoucherTypeCode",
                   v.voucher_no as "VoucherNumber", v.voucher_date as "VoucherDate", v.amount as "Amount", v.customer_id as "CustomerId",
                   v.vendor_id as "VendorId", v.staff_id as "StaffId", p.name as "ContactName", v.payment_mode as "PaymentMode", v.ref_no as "ReferenceNumber", v.description as "Description",
                   v.status as "StatusCode", v.void_reason as "VoidReason", v.voided_at as "VoidedAt", v.created_at as "CreatedAt",
                   v.row_ver as "RowVersion", v.journal_entry_id as "JournalEntryId"
              from acct.vouchers v
              join core.stores s on s.company_id = v.company_id and s.id = v.store_id
              left join contacts.v_contact_directory p on p.company_id = v.company_id
               and p.kind_code = (case when v.customer_id is not null then 'CUSTOMER' when v.vendor_id is not null then 'VENDOR' else 'STAFF' end)
               and p.id = coalesce(v.customer_id, v.vendor_id, v.staff_id)
             where v.company_id = {companyId} and v.id = {voucherId}
            """).SingleOrDefaultAsync(cancellationToken);
        if (header is null) return null;

        var lines = await databaseContext.Database.SqlQuery<VoucherLineRow>($"""
            select a.code as "AccountCode", a.name as "AccountTitle", l.customer_id as "CustomerId", l.vendor_id as "VendorId", l.staff_id as "StaffId", p.name as "ContactName",
                   l.debit as "DebitAmount", l.credit as "CreditAmount", coalesce(l.description, e.description) as "Description"
              from acct.journal_lines l
              join acct.journal_entries e on e.company_id = l.company_id and e.id = l.entry_id and e.entry_date = l.entry_date
              join acct.accounts a on a.company_id = l.company_id and a.id = l.account_id
              left join contacts.v_contact_directory p on p.company_id = l.company_id
                   and p.kind_code = (case when l.customer_id is not null then 'CUSTOMER' when l.vendor_id is not null then 'VENDOR' else 'STAFF' end)
                   and p.id = coalesce(l.customer_id, l.vendor_id, l.staff_id)
             where l.company_id = {companyId} and l.entry_id = {header.JournalEntryId} and l.entry_date = {header.VoucherDate}
             order by l.line_no
            """).ToListAsync(cancellationToken);

        var allocations = await databaseContext.Database.SqlQuery<VoucherAllocationRow>($"""
            select 'PURCHASE' as "InvoiceKindCode", va.purchase_invoice_id as "InvoiceId", pi.invoice_no as "InvoiceNumber", va.amount as "Amount"
              from acct.voucher_allocations va
              join purch.invoices pi on pi.company_id = va.company_id and pi.id = va.purchase_invoice_id
             where va.company_id = {companyId} and va.voucher_id = {voucherId}
            union all
            select 'SALE', va.sale_invoice_id, si.invoice_no, va.amount
              from acct.voucher_allocations va
              join sales.invoices si on si.company_id = va.company_id and si.id = va.sale_invoice_id
             where va.company_id = {companyId} and va.voucher_id = {voucherId}
            """).ToListAsync(cancellationToken);

        return new VoucherResponse(
            header.Id, header.StoreId, header.StoreName, header.VoucherTypeCode, header.VoucherNumber, header.VoucherDate, header.Amount,
            header.CustomerId, header.VendorId, header.StaffId, header.ContactName, header.PaymentMode, header.ReferenceNumber, header.Description, header.StatusCode,
            header.VoidReason, header.VoidedAt, header.CreatedAt, header.RowVersion,
            lines.Select(line => new VoucherLineResponse(line.AccountCode, line.AccountTitle, line.CustomerId, line.VendorId, line.StaffId, line.ContactName,
                                                         line.DebitAmount, line.CreditAmount, line.Description)).ToList(),
            allocations.Select(allocation => new VoucherAllocationResponse(allocation.InvoiceKindCode, allocation.InvoiceId,
                                                                           allocation.InvoiceNumber, allocation.Amount)).ToList());
    }

    public async Task<Guid?> FindIdByNumberAsync(string voucherTypeCode, long voucherNumber, Guid storeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var matches = await databaseContext.Database.SqlQuery<Guid>($"""
            select v.id as "Value" from acct.vouchers v
             where v.company_id = {companyId} and v.store_id = {storeId} and v.voucher_type = {voucherTypeCode} and v.voucher_no = {voucherNumber}
            """).ToListAsync(cancellationToken);
        return matches.Count == 0 ? null : matches[0];
    }

    public async Task<PagedResult<VoucherListItemResponse>> SearchAsync(VoucherSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var filterParameters = BuildFilterParameters(filter, pageRequest.SearchText);

        var totalCount = await databaseContext.Database
            .SqlQueryRaw<long>($"select count(*) as \"Value\" {VoucherFilterFromAndWhere}", filterParameters)
            .SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<VoucherListItemResponse>.Empty(pageRequest);

        var direction = pageRequest.SortDirection == SortDirection.Descending ? "desc" : "asc";
        var primaryOrder = pageRequest.SortField is { } sortField && SortableColumns.TryGetValue(sortField, out var column)
            ? $"{column} {direction}, " : "";
        var parameters = filterParameters.Concat(new object?[] { pageRequest.PageSize, pageRequest.SkippedItemCount }).ToArray();

        var rows = await databaseContext.Database.SqlQueryRaw<VoucherListRow>($$"""
            select {{VoucherListColumns}} {{VoucherFilterFromAndWhere}}
             order by {{primaryOrder}}v.voucher_date desc, v.voucher_no desc, v.id
             limit {11} offset {12}
            """, parameters).ToListAsync(cancellationToken);
        return new PagedResult<VoucherListItemResponse>(rows.Select(ToListItem).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    public async Task<VoucherReportResponse> BuildReportAsync(VoucherSearchFilter filter, int rowLimit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var filterParameters = BuildFilterParameters(filter, null);

        // The totals are taken over every matching voucher, not only the rows returned, so a truncated report still shows the right figures.
        var totals = await databaseContext.Database.SqlQueryRaw<VoucherTotalsRow>($"""
            select count(*) filter (where v.status = 'POSTED') as "PostedVoucherCount",
                   count(*) filter (where v.status = 'VOID') as "VoidVoucherCount",
                   cast(coalesce(sum(v.amount) filter (where v.status = 'POSTED'), 0) as bigint) as "TotalAmount"
            {VoucherFilterFromAndWhere}
            """, filterParameters).SingleAsync(cancellationToken);

        var parameters = filterParameters.Concat(new object?[] { rowLimit + 1 }).ToArray();
        var rows = await databaseContext.Database.SqlQueryRaw<VoucherListRow>($$"""
            select {{VoucherListColumns}} {{VoucherFilterFromAndWhere}}
             order by v.voucher_date, v.voucher_no, v.id
             limit {11}
            """, parameters).ToListAsync(cancellationToken);

        var isTruncated = rows.Count > rowLimit;
        return new VoucherReportResponse(rows.Take(rowLimit).Select(ToListItem).ToList(),
                                         totals.PostedVoucherCount, totals.VoidVoucherCount, totals.TotalAmount, isTruncated);
    }

    public async Task<bool> UpdateDescriptionAsync(Guid voucherId, string? description, long expectedRowVersion, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var changedRows = await databaseContext.Database.ExecuteSqlAsync($"""
            update acct.vouchers set description = {description}
             where company_id = {companyId} and id = {voucherId} and row_ver = {expectedRowVersion}
            """, cancellationToken);
        return changedRows == 1;
    }

    public async Task VoidAsync(Guid voucherId, string reason, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        await databaseContext.Database.SqlQuery<Guid>($"""
            select acct.void_voucher({companyId}, {voucherId}, {reason}) as "Value"
            """).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VoucherTargetOptionResponse>> SearchTargetsAsync(
        string kindCode, string? searchText, string? groupCode, int limit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var pattern = searchText is null ? null : LikePattern.Contains(searchText);

        if (kindCode == VoucherTargetKindCodes.Account)
        {
            // Accounts a voucher may post to: active, postable, not the subledger control account (customers, vendors and staff are chosen as such) and not the stock
            // accounts (stock is posted by stock documents). The same rules acct.resolve_voucher_side enforces.
            var accountRows = await databaseContext.Database.SqlQuery<TargetRow>($"""
                select a.id as "AccountId", cast(null as uuid) as "CustomerId", cast(null as uuid) as "VendorId", cast(null as uuid) as "StaffId", a.code as "Code", a.name as "Title",
                       coalesce(g.name, 'Group ' || a.group_code) as "TypeTitle", cast(coalesce(b.balance, 0) as bigint) as "Balance"
                  from acct.accounts a
                  left join acct.account_groups g on g.company_id = a.company_id and g.group_code = a.group_code
                  left join (select account_id, sum(debit) - sum(credit) as balance
                               from acct.account_month_balances where company_id = {companyId} group by account_id) b on b.account_id = a.id
                 where a.company_id = {companyId} and a.is_active and a.is_postable and not a.is_subledger_control
                   and coalesce(a.system_role, '') not in ('STOCK', 'TRANSIT')
                   and ({groupCode}::text is null or a.group_code = {groupCode})
                   and ({pattern}::text is null or a.code ilike {pattern} escape '\' or a.name ilike {pattern} escape '\')
                 order by a.code
                 limit {limit}
                """).ToListAsync(cancellationToken);
            return accountRows.Select(ToTargetOption(VoucherTargetKindCodes.Account)).ToList();
        }

        // The table and the balance table are chosen from fixed names (never from user input), so they are safe to put into the SQL text.
        var (contactTable, balanceTable, idColumn) = kindCode switch
        {
            VoucherTargetKindCodes.Customer => ("contacts.customers", "contacts.customer_balances", "customer_id"),
            VoucherTargetKindCodes.Vendor => ("contacts.vendors", "contacts.vendor_balances", "vendor_id"),
            _ => ("contacts.staff", "contacts.staff_balances", "staff_id"),
        };
        var typeTitle = kindCode == VoucherTargetKindCodes.Customer ? "Customer" : kindCode == VoucherTargetKindCodes.Vendor ? "Vendor" : "Staff";
        var contactRows = await databaseContext.Database.SqlQueryRaw<ContactTargetRow>("""
            select p.id as "ContactId", p.code as "Code", p.name as "Title", cast(coalesce(pb.debit_total - pb.credit_total, 0) as bigint) as "Balance"
              from __CONTACT_TABLE__ p
              left join __BALANCE_TABLE__ pb on pb.company_id = p.company_id and pb.__ID_COLUMN__ = p.id
             where p.company_id = {0} and not p.is_deleted and p.status = 'ACTIVE'
               and (cast({1} as text) is null or p.code ilike cast({1} as text) escape '\' or p.name ilike cast({1} as text) escape '\'
                    or p.phone ilike cast({1} as text) escape '\')
             order by p.name
             limit {2}
            """.Replace("__CONTACT_TABLE__", contactTable).Replace("__BALANCE_TABLE__", balanceTable).Replace("__ID_COLUMN__", idColumn),
            companyId, (object?)pattern ?? DBNull.Value, limit).ToListAsync(cancellationToken);
        return contactRows.Select(row => new VoucherTargetOptionResponse(
            kindCode, null,
            kindCode == VoucherTargetKindCodes.Customer ? row.ContactId : null,
            kindCode == VoucherTargetKindCodes.Vendor ? row.ContactId : null,
            kindCode == VoucherTargetKindCodes.Staff ? row.ContactId : null,
            row.Code, row.Title, typeTitle, Math.Abs(row.Balance), BalanceSideCodes.SideOf(row.Balance))).ToList();
    }

    private object?[] BuildFilterParameters(VoucherSearchFilter filter, string? searchText) =>
    [
        currentUser.CompanyId, filter.VoucherTypeCode, filter.StoreId, filter.FromDate, filter.ToDate, filter.StatusCode,
        filter.CustomerId, filter.VendorId, filter.StaffId,
        searchText, searchText is null ? null : LikePattern.Contains(searchText),
    ];

    private static VoucherListItemResponse ToListItem(VoucherListRow row) =>
        new(row.Id, row.StoreId, row.StoreName, row.VoucherTypeCode, row.VoucherNumber, row.VoucherDate, row.Amount,
            row.DebitAccountTitle, row.CreditAccountTitle, row.ContactName, row.ReferenceNumber, row.Description, row.StatusCode);

    private static Func<TargetRow, VoucherTargetOptionResponse> ToTargetOption(string kindCode) =>
        row => new VoucherTargetOptionResponse(kindCode, row.AccountId, row.CustomerId, row.VendorId, row.StaffId, row.Code, row.Title, row.TypeTitle,
                                               Math.Abs(row.Balance), BalanceSideCodes.SideOf(row.Balance));

    private sealed class VoucherHeaderRow
    {
        public Guid Id { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public string VoucherTypeCode { get; set; } = "";
        public long VoucherNumber { get; set; }
        public DateOnly VoucherDate { get; set; }
        public long Amount { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? VendorId { get; set; }
        public Guid? StaffId { get; set; }
        public string? ContactName { get; set; }
        public string? PaymentMode { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public string StatusCode { get; set; } = "";
        public string? VoidReason { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public long RowVersion { get; set; }
        public Guid JournalEntryId { get; set; }
    }

    private sealed class VoucherLineRow
    {
        public string AccountCode { get; set; } = "";
        public string AccountTitle { get; set; } = "";
        public Guid? CustomerId { get; set; }
        public Guid? VendorId { get; set; }
        public Guid? StaffId { get; set; }
        public string? ContactName { get; set; }
        public long DebitAmount { get; set; }
        public long CreditAmount { get; set; }
        public string? Description { get; set; }
    }

    private sealed class VoucherAllocationRow
    {
        public string InvoiceKindCode { get; set; } = "";
        public Guid InvoiceId { get; set; }
        public long InvoiceNumber { get; set; }
        public long Amount { get; set; }
    }

    private sealed class VoucherListRow
    {
        public Guid Id { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public string VoucherTypeCode { get; set; } = "";
        public long VoucherNumber { get; set; }
        public DateOnly VoucherDate { get; set; }
        public long Amount { get; set; }
        public string DebitAccountTitle { get; set; } = "";
        public string CreditAccountTitle { get; set; } = "";
        public string? ContactName { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public string StatusCode { get; set; } = "";
    }

    private sealed class VoucherTotalsRow
    {
        public long PostedVoucherCount { get; set; }
        public long VoidVoucherCount { get; set; }
        public long TotalAmount { get; set; }
    }

    private sealed class ContactTargetRow
    {
        public Guid ContactId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public long Balance { get; set; }
    }

    private sealed class TargetRow
    {
        public Guid? AccountId { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? VendorId { get; set; }
        public Guid? StaffId { get; set; }
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string TypeTitle { get; set; } = "";
        public long Balance { get; set; }
    }
}
