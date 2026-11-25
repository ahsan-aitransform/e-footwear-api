using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Accounting.Repositories;

/// <summary>
/// Opening balances. Reads the balances in force from the view acct.v_live_opening_balances; every write goes through the
/// database function acct.post_opening_balance, which checks the OPENING_BALANCE permission, reverses the balance in force and
/// posts the new one. The application never inserts journal rows itself (Part 15).
/// </summary>
public sealed class OpeningBalanceRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IOpeningBalanceRepository
{
    public async Task<IReadOnlyList<OpeningBalanceRowResponse>> ListRowsAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;

        // Accounts of the store. Stock and in-transit accounts are left out (opening stock is entered through inventory),
        // and so are the subledger control account (customers, vendors and staff have their own lines) and the opening equity account itself.
        var accountRows = await databaseContext.Database.SqlQuery<OpeningRow>($"""
            select a.id as "AccountId", cast(null as uuid) as "CustomerId", cast(null as uuid) as "VendorId", cast(null as uuid) as "StaffId", a.code as "Code", a.name as "Name",
                   cast(null as text) as "AreaName",
                   coalesce(v.debit, 0) as "DebitAmount", coalesce(v.credit, 0) as "CreditAmount", v.entry_date as "EntryDate"
              from acct.accounts a
              left join acct.v_live_opening_balances v
                     on v.company_id = a.company_id and v.account_id = a.id and v.customer_id is null and v.vendor_id is null and v.staff_id is null
             where a.company_id = {companyId} and a.store_id = {storeId}
               and a.is_active and a.is_postable and not a.is_subledger_control
               and coalesce(a.system_role, '') not in ('STOCK', 'TRANSIT', 'OPENING_EQUITY')
             order by a.code
            """).ToListAsync(cancellationToken);

        var rows = accountRows.Select(ToResponse).ToList();
        if (storeId != AccountingScope.HeadOfficeStoreId) return rows;

        // Customers, vendors and staff are company-wide, so their opening balances are listed with the head office.
        var contactRows = await databaseContext.Database.SqlQuery<OpeningRow>($"""
            select * from (
                select cast(null as uuid) as "AccountId", p.id as "CustomerId", cast(null as uuid) as "VendorId", cast(null as uuid) as "StaffId",
                       p.code as "Code", p.name as "Name", c.name as "AreaName",
                       coalesce(v.debit, 0) as "DebitAmount", coalesce(v.credit, 0) as "CreditAmount", v.entry_date as "EntryDate"
                  from contacts.customers p
                  left join master.cities c on c.company_id = p.company_id and c.id = p.city_id
                  left join acct.v_live_opening_balances v on v.company_id = p.company_id and v.customer_id = p.id
                 where p.company_id = {companyId} and not p.is_deleted
                union all
                select cast(null as uuid), cast(null as uuid), p.id, cast(null as uuid), p.code, p.name, c.name,
                       coalesce(v.debit, 0), coalesce(v.credit, 0), v.entry_date
                  from contacts.vendors p
                  left join master.cities c on c.company_id = p.company_id and c.id = p.city_id
                  left join acct.v_live_opening_balances v on v.company_id = p.company_id and v.vendor_id = p.id
                 where p.company_id = {companyId} and not p.is_deleted
                union all
                select cast(null as uuid), cast(null as uuid), cast(null as uuid), p.id, p.code, p.name, c.name,
                       coalesce(v.debit, 0), coalesce(v.credit, 0), v.entry_date
                  from contacts.staff p
                  left join master.cities c on c.company_id = p.company_id and c.id = p.city_id
                  left join acct.v_live_opening_balances v on v.company_id = p.company_id and v.staff_id = p.id
                 where p.company_id = {companyId} and not p.is_deleted
            ) contact_rows
             order by "Code", "CustomerId", "VendorId", "StaffId"
            """).ToListAsync(cancellationToken);
        rows.AddRange(contactRows.Select(ToResponse));
        return rows;
    }

    public async Task PostAsync(Guid? accountId, ContactReference? contact, Guid storeId, long debitAmount, long creditAmount, DateOnly entryDate,
                                CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        await databaseContext.Database.ExecuteSqlAsync($"""
            select acct.post_opening_balance({companyId}, {accountId}, {contact?.KindCode}, {contact?.Id}, {storeId}, {debitAmount}, {creditAmount}, {entryDate})
            """, cancellationToken);
    }

    private static OpeningBalanceRowResponse ToResponse(OpeningRow row) =>
        new(row.AccountId, row.CustomerId, row.VendorId, row.StaffId, row.Code, row.Name, row.AreaName, row.DebitAmount, row.CreditAmount, row.EntryDate);

    private sealed class OpeningRow
    {
        public Guid? AccountId { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? VendorId { get; set; }
        public Guid? StaffId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? AreaName { get; set; }
        public long DebitAmount { get; set; }
        public long CreditAmount { get; set; }
        public DateOnly? EntryDate { get; set; }
    }
}
