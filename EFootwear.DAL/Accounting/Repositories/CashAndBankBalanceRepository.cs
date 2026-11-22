using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Accounting.Repositories;

/// <summary>
/// Cash and bank balances per account and store, taken from journal lines (never from a stored balance). The postings of a store carry that store on
/// every line, even when the account itself belongs to the head office, so grouping by the line's store gives the cash position of each store.
/// </summary>
public sealed class CashAndBankBalanceRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : ICashAndBankBalanceRepository
{
    private const string CashKindCode = "CASH";
    private const string BankKindCode = "BANK";

    public async Task<CashAndBankBalancesResponse> GetAsync(DateOnly asOfDate, Guid? storeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        var rows = await databaseContext.Database.SqlQuery<BalanceRow>($"""
            select a.id as "AccountId", a.code as "AccountCode", a.name as "AccountTitle",
                   case when a.system_role = 'BANK' or a.group_code = '102' then 'BANK' else 'CASH' end as "KindCode",
                   l.store_id as "StoreId", s.name as "StoreName",
                   cast(sum(l.debit - l.credit) as bigint) as "Balance"
              from acct.accounts a
              join acct.journal_lines l on l.company_id = a.company_id and l.account_id = a.id and l.entry_date <= {asOfDate}
              join core.stores s on s.company_id = l.company_id and s.id = l.store_id
             where a.company_id = {companyId} and a.is_active
               and (a.system_role in ('CASH', 'BANK') or a.group_code in ('101', '102'))
               and (cast({storeId} as uuid) is null or l.store_id = cast({storeId} as uuid))
             group by a.id, a.code, a.name, a.system_role, a.group_code, l.store_id, s.name
             order by a.code, s.name
            """).ToListAsync(cancellationToken);

        var accounts = rows
            .Select(row => new CashAndBankAccountBalanceResponse(row.AccountId, row.AccountCode, row.AccountTitle, row.KindCode, row.StoreId,
                                                                 row.StoreName, Math.Abs(row.Balance), BalanceSideCodes.SideOf(row.Balance)))
            .ToList();
        return new CashAndBankBalancesResponse(
            asOfDate, storeId, accounts,
            rows.Where(row => row.KindCode == CashKindCode).Sum(row => row.Balance),
            rows.Where(row => row.KindCode == BankKindCode).Sum(row => row.Balance));
    }

    private sealed class BalanceRow
    {
        public Guid AccountId { get; set; }
        public string AccountCode { get; set; } = "";
        public string AccountTitle { get; set; } = "";
        public string KindCode { get; set; } = "";
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public long Balance { get; set; }
    }
}
