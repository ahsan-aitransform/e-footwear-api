using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Payroll.Abstractions;
using EFootwear.BLL.Payroll.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Payroll.Repositories;

/// <summary>
/// The Staff Salary and Salary Outstanding reports. Both read the salary sheet lines that are not cancelled (hr.payroll_lines; Salary Outstanding through
/// hr.v_salary_outstanding), one row per line, so no join can multiply a financial figure. The totals are taken over every matching line, not only the rows
/// returned. The payroll tables are isolated by company and store; the queries also name the caller's stores.
/// </summary>
public sealed class PayrollReportRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IPayrollReportRepository
{
    // Parameters: 0 company, 1 store, 2 first month, 3 last month, 4 employee, 5 may use every store, 6 the stores the caller may use.
    private const string StaffSalaryFromAndWhere = """
        from hr.payroll_lines l
        join party.parties p on p.company_id = l.company_id and p.id = l.employee_party_id
        join core.stores s on s.company_id = l.company_id and s.id = l.store_id
        left join party.staff_profiles sp on sp.company_id = l.company_id and sp.party_id = l.employee_party_id
        where l.company_id = {0} and l.status <> 'CANCELLED'
          and l.period_month between cast({2} as date) and cast({3} as date)
          and (cast({1} as uuid) is null or l.store_id = cast({1} as uuid))
          and (cast({4} as uuid) is null or l.employee_party_id = cast({4} as uuid))
          and (cast({5} as boolean) or l.store_id = any(cast({6} as uuid[])))
        """;

    // Parameters: 0 company, 1 store, 2 employee, 3 may use every store, 4 the stores the caller may use.
    private const string SalaryOutstandingFromAndWhere = """
        from hr.v_salary_outstanding v
        join core.stores s on s.company_id = v.company_id and s.id = v.store_id
        where v.company_id = {0} and v.status = 'APPROVED' and v.outstanding_amount > 0
          and (cast({1} as uuid) is null or v.store_id = cast({1} as uuid))
          and (cast({2} as uuid) is null or v.employee_party_id = cast({2} as uuid))
          and (cast({3} as boolean) or v.store_id = any(cast({4} as uuid[])))
        """;

    public async Task<StaffSalaryReportResponse> GetStaffSalaryReportAsync(StaffSalaryReportRequest request, int rowLimit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        object?[] filterParameters =
        [
            currentUser.CompanyId, request.StoreId, request.FromMonth, request.ToMonth, request.EmployeePartyId,
            currentUser.HasAccessToAllStores, currentUser.AssignedStoreIds.ToArray(),
        ];

        var totals = await databaseContext.Database.SqlQueryRaw<StaffSalaryTotalsRow>($$"""
            select cast(coalesce(sum(l.base_salary), 0) as bigint) as "TotalBaseSalary",
                   cast(coalesce(sum(l.commission_amount), 0) as bigint) as "TotalCommission",
                   cast(coalesce(sum(l.incentive_amount), 0) as bigint) as "TotalIncentive",
                   cast(coalesce(sum(l.deduction_amount), 0) as bigint) as "TotalDeduction",
                   cast(coalesce(sum(l.net_pay), 0) as bigint) as "TotalNetPay",
                   cast(coalesce(sum(l.paid_amount), 0) as bigint) as "TotalPaid",
                   cast(coalesce(sum(l.outstanding_amount), 0) as bigint) as "TotalOutstanding",
                   count(*) as "LineCount"
            {{StaffSalaryFromAndWhere}}
            """, filterParameters).SingleAsync(cancellationToken);

        var parameters = filterParameters.Concat(new object?[] { rowLimit + 1 }).ToArray();
        var rows = await databaseContext.Database.SqlQueryRaw<StaffSalaryRow>($$"""
            select l.period_month as "PeriodMonth", l.store_id as "StoreId", s.name as "StoreName", l.employee_party_id as "EmployeePartyId",
                   p.code as "EmployeeCode", p.name as "EmployeeName", sp.designation as "Designation", l.base_salary as "BaseSalary",
                   l.commission_amount as "CommissionAmount", l.incentive_amount as "IncentiveAmount", l.deduction_amount as "DeductionAmount",
                   l.net_pay as "NetPay", l.paid_amount as "PaidAmount", l.outstanding_amount as "OutstandingAmount", l.status as "StatusCode"
            {{StaffSalaryFromAndWhere}}
             order by l.period_month desc, s.name, p.name, l.id
             limit {7}
            """, parameters).ToListAsync(cancellationToken);

        return new StaffSalaryReportResponse(
            rows.Take(rowLimit).Select(row => new StaffSalaryReportRowResponse(
                row.PeriodMonth, row.StoreId, row.StoreName, row.EmployeePartyId, row.EmployeeCode, row.EmployeeName, row.Designation, row.BaseSalary,
                row.CommissionAmount, row.IncentiveAmount, row.DeductionAmount, row.NetPay, row.PaidAmount, row.OutstandingAmount, row.StatusCode)).ToList(),
            totals.TotalBaseSalary, totals.TotalCommission, totals.TotalIncentive, totals.TotalDeduction, totals.TotalNetPay, totals.TotalPaid,
            totals.TotalOutstanding, totals.LineCount, rows.Count > rowLimit);
    }

    public async Task<SalaryOutstandingReportResponse> GetSalaryOutstandingReportAsync(SalaryOutstandingReportRequest request, int rowLimit, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        object?[] filterParameters =
        [
            currentUser.CompanyId, request.StoreId, request.EmployeePartyId, currentUser.HasAccessToAllStores, currentUser.AssignedStoreIds.ToArray(),
        ];

        var totals = await databaseContext.Database.SqlQueryRaw<OutstandingTotalsRow>($$"""
            select cast(coalesce(sum(v.net_pay), 0) as bigint) as "TotalNetPay",
                   cast(coalesce(sum(v.paid_amount), 0) as bigint) as "TotalPaid",
                   cast(coalesce(sum(v.outstanding_amount), 0) as bigint) as "TotalOutstanding",
                   count(*) as "LineCount"
            {{SalaryOutstandingFromAndWhere}}
            """, filterParameters).SingleAsync(cancellationToken);

        var parameters = filterParameters.Concat(new object?[] { rowLimit + 1 }).ToArray();
        var rows = await databaseContext.Database.SqlQueryRaw<OutstandingRow>($$"""
            select v.period_month as "PeriodMonth", v.store_id as "StoreId", s.name as "StoreName", v.employee_party_id as "EmployeePartyId",
                   v.employee_name as "EmployeeName", v.net_pay as "NetPay", v.paid_amount as "PaidAmount", v.outstanding_amount as "OutstandingAmount"
            {{SalaryOutstandingFromAndWhere}}
             order by v.period_month, s.name, v.employee_name
             limit {5}
            """, parameters).ToListAsync(cancellationToken);

        return new SalaryOutstandingReportResponse(
            rows.Take(rowLimit).Select(row => new SalaryOutstandingReportRowResponse(
                row.PeriodMonth, row.StoreId, row.StoreName, row.EmployeePartyId, row.EmployeeName, row.NetPay, row.PaidAmount, row.OutstandingAmount)).ToList(),
            totals.TotalNetPay, totals.TotalPaid, totals.TotalOutstanding, totals.LineCount, rows.Count > rowLimit);
    }

    private sealed class StaffSalaryTotalsRow
    {
        public long TotalBaseSalary { get; set; }
        public long TotalCommission { get; set; }
        public long TotalIncentive { get; set; }
        public long TotalDeduction { get; set; }
        public long TotalNetPay { get; set; }
        public long TotalPaid { get; set; }
        public long TotalOutstanding { get; set; }
        public long LineCount { get; set; }
    }

    private sealed class StaffSalaryRow
    {
        public DateOnly PeriodMonth { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public Guid EmployeePartyId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string? Designation { get; set; }
        public long BaseSalary { get; set; }
        public long CommissionAmount { get; set; }
        public long IncentiveAmount { get; set; }
        public long DeductionAmount { get; set; }
        public long NetPay { get; set; }
        public long PaidAmount { get; set; }
        public long OutstandingAmount { get; set; }
        public string StatusCode { get; set; } = "";
    }

    private sealed class OutstandingTotalsRow
    {
        public long TotalNetPay { get; set; }
        public long TotalPaid { get; set; }
        public long TotalOutstanding { get; set; }
        public long LineCount { get; set; }
    }

    private sealed class OutstandingRow
    {
        public DateOnly PeriodMonth { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public Guid EmployeePartyId { get; set; }
        public string EmployeeName { get; set; } = "";
        public long NetPay { get; set; }
        public long PaidAmount { get; set; }
        public long OutstandingAmount { get; set; }
    }
}
