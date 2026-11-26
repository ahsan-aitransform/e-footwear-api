using EFootwear.BLL.Commissions.Contracts;

namespace EFootwear.DAL.Commissions.Repositories;

/// <summary>
/// The SQL every commission entry query shares, so the entries list, the totals and the reports filter and read the entries in exactly the same way.
/// Entries are read one row each (hr.commission_entries joined to single rows of the store, the staff member, the plan and the invoice), so a join can never
/// multiply a figure. The salary month comes from the salary sheet line that took the entry, when one did.
/// </summary>
internal static class CommissionEntrySql
{
    /// <summary>
    /// Parameters: 0 company, 1 store, 2 staff, 3 status, 4 first sale date, 5 last sale date, 6 may use every store, 7 the stores the caller may use,
    /// 8 search pattern. Cancelled entries only appear when asked for by status.
    /// </summary>
    public const string FromAndWhere = """
        from hr.commission_entries e
        join core.stores s on s.company_id = e.company_id and s.id = e.store_id
        join contacts.staff p on p.company_id = e.company_id and p.id = e.staff_id
        join hr.commission_plans pl on pl.company_id = e.company_id and pl.id = e.plan_id
        join sales.invoices i on i.company_id = e.company_id and i.id = e.invoice_id
        left join hr.payroll_lines l on l.company_id = e.company_id and l.id = e.payroll_line_id
        where e.company_id = {0}
          and (cast({1} as uuid) is null or e.store_id = cast({1} as uuid))
          and (cast({2} as uuid) is null or e.staff_id = cast({2} as uuid))
          and ((cast({3} as text) is null and e.status <> 'CANCELLED') or e.status = cast({3} as text))
          and (cast({4} as date) is null or e.sale_date >= cast({4} as date))
          and (cast({5} as date) is null or e.sale_date <= cast({5} as date))
          and (cast({6} as boolean) or e.store_id = any(cast({7} as uuid[])))
          and (cast({8} as text) is null or p.name ilike cast({8} as text) escape '\' or p.code ilike cast({8} as text) escape '\'
               or pl.name ilike cast({8} as text) escape '\' or cast(i.invoice_no as text) ilike cast({8} as text) escape '\')
        """;

    public const string Columns = """
        e.id as "Id", e.store_id as "StoreId", s.name as "StoreName", e.staff_id as "StaffId", p.code as "StaffCode", p.name as "StaffName",
        e.plan_id as "PlanId", pl.name as "PlanName", e.sale_date as "SaleDate", e.invoice_id as "InvoiceId", i.invoice_no as "InvoiceNumber",
        e.entry_type as "EntryTypeCode", e.calc_type as "CalculationTypeCode", e.rate as "Rate", e.fixed_amount as "FixedAmountPerPiece",
        e.qty as "Quantity", e.base_amount as "BaseAmount", e.commission_amount as "CommissionAmount", e.status as "StatusCode",
        e.approved_at as "ApprovedAt", l.period_month as "SalaryMonth"
        """;

    internal sealed class EntryRow
    {
        public Guid Id { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public Guid StaffId { get; set; }
        public string StaffCode { get; set; } = "";
        public string StaffName { get; set; } = "";
        public Guid PlanId { get; set; }
        public string PlanName { get; set; } = "";
        public DateOnly SaleDate { get; set; }
        public Guid InvoiceId { get; set; }
        public long InvoiceNumber { get; set; }
        public string EntryTypeCode { get; set; } = "";
        public string CalculationTypeCode { get; set; } = "";
        public decimal? Rate { get; set; }
        public long? FixedAmountPerPiece { get; set; }
        public decimal Quantity { get; set; }
        public long BaseAmount { get; set; }
        public long CommissionAmount { get; set; }
        public string StatusCode { get; set; } = "";
        public DateTimeOffset? ApprovedAt { get; set; }
        public DateOnly? SalaryMonth { get; set; }
    }

    internal static CommissionEntryResponse ToResponse(EntryRow row) => new(
        row.Id, row.StoreId, row.StoreName, row.StaffId, row.StaffCode, row.StaffName, row.PlanId, row.PlanName, row.SaleDate, row.InvoiceId,
        row.InvoiceNumber, row.EntryTypeCode, row.CalculationTypeCode, row.Rate, row.FixedAmountPerPiece, row.Quantity, row.BaseAmount,
        row.CommissionAmount, row.StatusCode, row.ApprovedAt, row.SalaryMonth);
}
