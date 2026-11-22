using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Parties.Abstractions;
using EFootwear.BLL.Parties.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Parties.Repositories;

/// <summary>
/// Reads the party ledger: the journal lines that carry a party, with the number of the voucher or invoice that posted them and a
/// running balance per party. Read only. Row-level security limits the lines to the caller's company and stores, and the balances
/// are derived from the journal (never stored on the party).
/// </summary>
public sealed class PartyLedgerRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IPartyLedgerRepository
{
    public async Task<PartyLedgerResponse> GetLedgerAsync(PartyLedgerQuery query, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var partyId = query.PartyId;
        var fromDate = query.From;
        var toDate = query.To;
        var skippedItemCount = pageRequest.SkippedItemCount;
        var pageSize = pageRequest.PageSize;

        // Lines of the period, newest history last. The running balance is the sum over the party's whole history up to each line,
        // so a period that starts mid-way still shows the balance the party really had.
        var rows = await databaseContext.Database.SqlQuery<LedgerLineRow>($"""
            with lines as (
                select jl.id as line_id, jl.party_id, p.code as party_code, p.name as party_name, jl.entry_date,
                       je.source_type, coalesce(v.voucher_no, si.invoice_no, pi.invoice_no) as document_number,
                       coalesce(jl.description, je.description) as description, jl.debit, jl.credit,
                       je.posted_at, jl.line_no,
                       (sum(jl.debit - jl.credit) over (partition by jl.party_id
                                                       order by jl.entry_date, je.posted_at, jl.line_no, jl.id))::bigint as running_balance
                  from acct.journal_lines jl
                  join acct.journal_entries je on je.company_id = jl.company_id and je.id = jl.entry_id and je.entry_date = jl.entry_date
                  join party.parties p on p.company_id = jl.company_id and p.id = jl.party_id
                  left join acct.vouchers v on v.company_id = je.company_id and v.journal_entry_id = je.id
                  left join sales.invoices si on si.company_id = je.company_id and si.journal_entry_id = je.id
                  left join purch.invoices pi on pi.company_id = je.company_id and pi.journal_entry_id = je.id
                 where jl.company_id = {companyId}
                   and ({partyId}::uuid is null or jl.party_id = {partyId}::uuid)
                   and ({toDate}::date is null or jl.entry_date <= {toDate}::date)
            )
            select line_id as "LineId", party_id as "PartyId", party_code as "PartyCode", party_name as "PartyName",
                   entry_date as "EntryDate", source_type as "SourceType", document_number as "DocumentNumber",
                   description as "Description", debit as "DebitAmount", credit as "CreditAmount",
                   running_balance as "RunningBalanceAmount"
              from lines
             where ({fromDate}::date is null or entry_date >= {fromDate}::date)
             order by entry_date, posted_at, line_no, line_id
             offset {skippedItemCount} limit {pageSize}
            """).ToListAsync(cancellationToken);

        var totals = await databaseContext.Database.SqlQuery<TotalsRow>($"""
            select count(*) as "LineCount", coalesce(sum(jl.debit), 0)::bigint as "TotalDebitAmount", coalesce(sum(jl.credit), 0)::bigint as "TotalCreditAmount"
              from acct.journal_lines jl
             where jl.company_id = {companyId} and jl.party_id is not null
               and ({partyId}::uuid is null or jl.party_id = {partyId}::uuid)
               and ({fromDate}::date is null or jl.entry_date >= {fromDate}::date)
               and ({toDate}::date is null or jl.entry_date <= {toDate}::date)
            """).SingleAsync(cancellationToken);

        long openingBalanceAmount = 0;
        if (partyId is not null && fromDate is not null)
        {
            openingBalanceAmount = await databaseContext.Database.SqlQuery<long>($"""
                select coalesce(sum(jl.debit - jl.credit), 0)::bigint as "Value"
                  from acct.journal_lines jl
                 where jl.company_id = {companyId} and jl.party_id = {partyId} and jl.entry_date < {fromDate}::date
                """).SingleAsync(cancellationToken);
        }

        var lines = rows.Select(row => new PartyLedgerLineResponse(row.LineId, row.PartyId, row.PartyCode, row.PartyName, row.EntryDate, row.SourceType,
                                                                    row.DocumentNumber, row.Description, row.DebitAmount, row.CreditAmount,
                                                                    row.RunningBalanceAmount)).ToList();
        return new PartyLedgerResponse(partyId, openingBalanceAmount,
                                       new PagedResult<PartyLedgerLineResponse>(lines, pageRequest.PageNumber, pageRequest.PageSize, totals.LineCount),
                                       totals.TotalDebitAmount, totals.TotalCreditAmount);
    }

    private sealed class LedgerLineRow
    {
        public Guid LineId { get; set; }
        public Guid PartyId { get; set; }
        public string PartyCode { get; set; } = "";
        public string PartyName { get; set; } = "";
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
