using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Abstractions;
using EFootwear.BLL.Contacts.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Contacts.Repositories;

/// <summary>
/// Reads the ledger of customers, vendors or staff: the journal lines that carry one of them, with the number of the voucher or invoice that
/// posted them and a running balance per contact. Read only. Row-level security limits the lines to the caller's company and stores, and the
/// balances are derived from the journal (never stored on the contact).
/// </summary>
public sealed class ContactLedgerRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IContactLedgerRepository
{
    public async Task<ContactLedgerData> GetLedgerAsync(string kindCode, Guid? contactId, DateOnly? fromDate, DateOnly? toDate,
                                                        PageRequest pageRequest, CancellationToken cancellationToken)
    {
        // The column is chosen from fixed names (never from user input), so it is safe to put into the SQL text.
        var ownerColumn = kindCode switch
        {
            ContactKindCodes.Customer => "customer_id",
            ContactKindCodes.Vendor => "vendor_id",
            ContactKindCodes.Staff => "staff_id",
            _ => throw new ArgumentOutOfRangeException(nameof(kindCode), kindCode, "Unknown contact kind."),
        };
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var skippedItemCount = pageRequest.SkippedItemCount;
        var pageSize = pageRequest.PageSize;
        object[] filterParameters = [companyId, kindCode, (object?)contactId ?? DBNull.Value, (object?)fromDate ?? DBNull.Value, (object?)toDate ?? DBNull.Value];

        // Parameters: 0 company, 1 kind, 2 contact, 3 from, 4 to, 5 offset, 6 limit. The running balance is the sum over the contact's whole
        // history up to each line, so a period that starts mid-way still shows the balance the contact really had.
        var rows = await databaseContext.Database.SqlQueryRaw<LedgerLineRow>("""
            with lines as (
                select jl.id as line_id, jl.__OWNER__ as contact_id, p.code as contact_code, p.name as contact_name, jl.entry_date,
                       je.source_type, coalesce(v.voucher_no, si.invoice_no, pi.invoice_no) as document_number,
                       coalesce(jl.description, je.description) as description, jl.debit, jl.credit,
                       je.posted_at, jl.line_no,
                       (sum(jl.debit - jl.credit) over (partition by jl.__OWNER__
                                                       order by jl.entry_date, je.posted_at, jl.line_no, jl.id))::bigint as running_balance
                  from acct.journal_lines jl
                  join acct.journal_entries je on je.company_id = jl.company_id and je.id = jl.entry_id and je.entry_date = jl.entry_date
                  join contacts.v_contact_directory p on p.company_id = jl.company_id and p.kind_code = {1} and p.id = jl.__OWNER__
                  left join acct.vouchers v on v.company_id = je.company_id and v.journal_entry_id = je.id
                  left join sales.invoices si on si.company_id = je.company_id and si.journal_entry_id = je.id
                  left join purch.invoices pi on pi.company_id = je.company_id and pi.journal_entry_id = je.id
                 where jl.company_id = {0}
                   and (cast({2} as uuid) is null or jl.__OWNER__ = cast({2} as uuid))
                   and (cast({4} as date) is null or jl.entry_date <= cast({4} as date))
            )
            select line_id as "LineId", contact_id as "ContactId", contact_code as "ContactCode", contact_name as "ContactName",
                   entry_date as "EntryDate", source_type as "SourceType", document_number as "DocumentNumber",
                   description as "Description", debit as "DebitAmount", credit as "CreditAmount",
                   running_balance as "RunningBalanceAmount"
              from lines
             where (cast({3} as date) is null or entry_date >= cast({3} as date))
             order by entry_date, posted_at, line_no, line_id
             offset {5} limit {6}
            """.Replace("__OWNER__", ownerColumn), [.. filterParameters, skippedItemCount, pageSize]).ToListAsync(cancellationToken);

        var totals = await databaseContext.Database.SqlQueryRaw<TotalsRow>("""
            select count(*) as "LineCount", coalesce(sum(jl.debit), 0)::bigint as "TotalDebitAmount", coalesce(sum(jl.credit), 0)::bigint as "TotalCreditAmount"
              from acct.journal_lines jl
             where jl.company_id = {0} and jl.__OWNER__ is not null
               and (cast({2} as uuid) is null or jl.__OWNER__ = cast({2} as uuid))
               and (cast({3} as date) is null or jl.entry_date >= cast({3} as date))
               and (cast({4} as date) is null or jl.entry_date <= cast({4} as date))
            """.Replace("__OWNER__", ownerColumn), filterParameters).SingleAsync(cancellationToken);

        long openingBalanceAmount = 0;
        if (contactId is not null && fromDate is not null)
        {
            openingBalanceAmount = await databaseContext.Database.SqlQueryRaw<long>("""
                select coalesce(sum(jl.debit - jl.credit), 0)::bigint as "Value"
                  from acct.journal_lines jl
                 where jl.company_id = {0} and jl.__OWNER__ = {2} and jl.entry_date < cast({3} as date)
                """.Replace("__OWNER__", ownerColumn), filterParameters).SingleAsync(cancellationToken);
        }

        var lines = rows.Select(row => new ContactLedgerLine(row.LineId, row.ContactId, row.ContactCode, row.ContactName, row.EntryDate, row.SourceType,
                                                             row.DocumentNumber, row.Description, row.DebitAmount, row.CreditAmount,
                                                             row.RunningBalanceAmount)).ToList();
        return new ContactLedgerData(openingBalanceAmount, lines, totals.LineCount, totals.TotalDebitAmount, totals.TotalCreditAmount);
    }

    private sealed class LedgerLineRow
    {
        public Guid LineId { get; set; }
        public Guid ContactId { get; set; }
        public string ContactCode { get; set; } = "";
        public string ContactName { get; set; } = "";
        public DateOnly EntryDate { get; set; }
        public string SourceType { get; set; } = "";
        public long? DocumentNumber { get; set; }
        public string? Description { get; set; }
        public long DebitAmount { get; set; }
        public long CreditAmount { get; set; }
        public long RunningBalanceAmount { get; set; }
    }

    private sealed class TotalsRow
    {
        public long LineCount { get; set; }
        public long TotalDebitAmount { get; set; }
        public long TotalCreditAmount { get; set; }
    }
}
