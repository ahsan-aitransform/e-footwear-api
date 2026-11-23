using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Payroll.Abstractions;
using EFootwear.BLL.Payroll.Contracts;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Payroll.Repositories;

/// <summary>
/// Salary sheets, adjustments and salary payments. Writes call the baseline functions hr.build_payroll_run, hr.approve_payroll_run and
/// hr.pay_salary_once (migration 0800); they check PAYROLL_PREPARE / PAYROLL_APPROVE again inside the database. Reads are plain SQL; the payroll
/// tables are isolated by company and store, so a caller never sees another store's sheet.
/// </summary>
public sealed class PayrollRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : IPayrollRepository
{
    private static readonly IReadOnlyDictionary<string, string> PaymentSortableColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["date"] = "sp.paid_on",
        ["amount"] = "sp.amount",
        ["name"] = "p.name",
        ["month"] = "l.period_month",
    };

    // Parameters: 0 company, 1 store, 2 employee, 3 first date, 4 last date, 5 search pattern, 6 may use every store, 7 the stores the caller may use.
    private const string PaymentFromAndWhere = """
        from hr.salary_payments sp
        join hr.payroll_lines l on l.company_id = sp.company_id and l.id = sp.payroll_line_id
        join party.parties p on p.company_id = sp.company_id and p.id = sp.employee_party_id
        join core.stores s on s.company_id = sp.company_id and s.id = sp.store_id
        left join sec.users paid_by_user on paid_by_user.company_id = sp.company_id and paid_by_user.id = sp.paid_by
        where sp.company_id = {0}
          and (cast({1} as uuid) is null or sp.store_id = cast({1} as uuid))
          and (cast({2} as uuid) is null or sp.employee_party_id = cast({2} as uuid))
          and (cast({3} as date) is null or sp.paid_on >= cast({3} as date))
          and (cast({4} as date) is null or sp.paid_on <= cast({4} as date))
          and (cast({5} as text) is null or p.name ilike cast({5} as text) escape '\' or sp.reference ilike cast({5} as text) escape '\')
          and (cast({6} as boolean) or sp.store_id = any(cast({7} as uuid[])))
        """;

    private const string PaymentColumns = """
        sp.id as "Id", sp.payroll_line_id as "PayrollLineId", sp.employee_party_id as "EmployeePartyId", p.name as "EmployeeName",
        sp.store_id as "StoreId", s.name as "StoreName", l.period_month as "PeriodMonth", sp.paid_on as "PaidOn", sp.amount as "Amount",
        sp.method as "MethodCode", sp.reference as "Reference", sp.paid_by as "PaidByUserId", paid_by_user.full_name as "PaidByName",
        sp.created_at as "CreatedAt"
        """;

    public async Task<PayrollSheetResponse?> FindSheetAsync(Guid storeId, DateOnly periodMonth, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var header = await databaseContext.Database.SqlQuery<SheetHeaderRow>($"""
            select r.id as "RunId", r.store_id as "StoreId", s.name as "StoreName", r.period_month as "PeriodMonth", r.status as "StatusCode",
                   r.approved_by as "ApprovedByUserId", u.full_name as "ApprovedByName", r.approved_at as "ApprovedAt", r.row_ver as "RowVersion"
              from hr.payroll_runs r
              join core.stores s on s.company_id = r.company_id and s.id = r.store_id
              left join sec.users u on u.company_id = r.company_id and u.id = r.approved_by
             where r.company_id = {companyId} and r.store_id = {storeId} and r.period_month = {periodMonth}
            """).SingleOrDefaultAsync(cancellationToken);
        return header is null ? null : await LoadSheetAsync(header, cancellationToken);
    }

    public async Task<PayrollSheetResponse?> FindSheetByRunIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var header = await databaseContext.Database.SqlQuery<SheetHeaderRow>($"""
            select r.id as "RunId", r.store_id as "StoreId", s.name as "StoreName", r.period_month as "PeriodMonth", r.status as "StatusCode",
                   r.approved_by as "ApprovedByUserId", u.full_name as "ApprovedByName", r.approved_at as "ApprovedAt", r.row_ver as "RowVersion"
              from hr.payroll_runs r
              join core.stores s on s.company_id = r.company_id and s.id = r.store_id
              left join sec.users u on u.company_id = r.company_id and u.id = r.approved_by
             where r.company_id = {companyId} and r.id = {runId}
            """).SingleOrDefaultAsync(cancellationToken);
        return header is null ? null : await LoadSheetAsync(header, cancellationToken);
    }

    public async Task<Guid> BuildSheetAsync(Guid storeId, DateOnly periodMonth, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Database.SqlQuery<Guid>($"""
            select hr.build_payroll_run({companyId}, {storeId}, {periodMonth}, {userId}) as "Value"
            """).SingleAsync(cancellationToken);
    }

    public async Task<Guid> ApproveSheetAsync(Guid runId, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Database.SqlQuery<Guid>($"""
            select hr.approve_payroll_run({companyId}, {runId}, {userId}) as "Value"
            """).SingleAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollAdjustmentResponse>> ListAdjustmentsAsync(Guid storeId, DateOnly periodMonth, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<AdjustmentRow>($"""
            select a.id as "Id", a.employee_party_id as "EmployeePartyId", p.name as "EmployeeName", a.period_month as "PeriodMonth",
                   a.adjustment_type as "AdjustmentTypeCode", a.amount as "Amount", a.reason as "Reason",
                   (a.payroll_line_id is not null) as "IsOnSalarySheet", a.created_at as "CreatedAt"
              from hr.payroll_adjustments a
              join party.parties p on p.company_id = a.company_id and p.id = a.employee_party_id
              join party.staff_profiles sp on sp.company_id = a.company_id and sp.party_id = a.employee_party_id
             where a.company_id = {companyId} and a.period_month = {periodMonth} and sp.current_store_id = {storeId}
             order by p.name, a.created_at, a.id
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new PayrollAdjustmentResponse(row.Id, row.EmployeePartyId, row.EmployeeName, row.PeriodMonth, row.AdjustmentTypeCode,
                                                                row.Amount, row.Reason, row.IsOnSalarySheet, row.CreatedAt)).ToList();
    }

    public async Task<bool> HasApprovedLineAsync(Guid employeePartyId, DateOnly periodMonth, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Database.SqlQuery<bool>($"""
            select exists (select 1 from hr.payroll_lines l
                            where l.company_id = {companyId} and l.employee_party_id = {employeePartyId}
                              and l.period_month = {periodMonth} and l.status in ('APPROVED', 'PAID')) as "Value"
            """).SingleAsync(cancellationToken);
    }

    public async Task<Guid> AddAdjustmentAsync(CreatePayrollAdjustmentRequest request, DateOnly periodMonth, Guid userId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        // INSERT ... RETURNING cannot be wrapped in a sub-select, so the rows are read with ToListAsync (no LINQ operator is added to the SQL).
        var insertedIds = await databaseContext.Database.SqlQuery<Guid>($"""
            insert into hr.payroll_adjustments (company_id, employee_party_id, period_month, adjustment_type, amount, reason, created_by)
            values ({companyId}, {request.EmployeePartyId}, {periodMonth}, {request.AdjustmentTypeCode}, {request.Amount}, {request.Reason}, {userId})
            returning id as "Value"
            """).ToListAsync(cancellationToken);
        return insertedIds.Single();
    }

    public async Task<PayrollLineSummary?> FindLineAsync(Guid lineId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQuery<LineSummaryRow>($"""
            select l.id as "LineId", l.store_id as "StoreId", l.employee_party_id as "EmployeePartyId", l.period_month as "PeriodMonth",
                   l.status as "StatusCode", l.outstanding_amount as "OutstandingAmount"
              from hr.payroll_lines l
             where l.company_id = {companyId} and l.id = {lineId}
            """).ToListAsync(cancellationToken);
        return rows.Count == 0
            ? null
            : new PayrollLineSummary(rows[0].LineId, rows[0].StoreId, rows[0].EmployeePartyId, rows[0].PeriodMonth, rows[0].StatusCode, rows[0].OutstandingAmount);
    }

    public async Task<Guid> PaySalaryAsync(SalaryPaymentWriteRecord payment, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Database.SqlQuery<Guid>($"""
            select hr.pay_salary_once({companyId}, {payment.LineId}, {payment.Amount}, {payment.MethodCode}, {payment.PaidOn},
                                      {payment.UserId}, {payment.Reference}, {payment.ClientTransactionId}) as "Value"
            """).SingleAsync(cancellationToken);
    }

    public async Task<SalaryPaymentResponse?> FindPaymentAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQueryRaw<PaymentRow>($$"""
            select {{PaymentColumns}}
            from hr.salary_payments sp
            join hr.payroll_lines l on l.company_id = sp.company_id and l.id = sp.payroll_line_id
            join party.parties p on p.company_id = sp.company_id and p.id = sp.employee_party_id
            join core.stores s on s.company_id = sp.company_id and s.id = sp.store_id
            left join sec.users paid_by_user on paid_by_user.company_id = sp.company_id and paid_by_user.id = sp.paid_by
            where sp.company_id = {0} and sp.id = {1}
            """, companyId, paymentId).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : ToPaymentResponse(rows[0]);
    }

    public async Task<SalaryPaymentResponse?> FindPaymentByClientTransactionAsync(Guid clientTransactionId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQueryRaw<PaymentRow>($$"""
            select {{PaymentColumns}}
            from hr.salary_payment_client_ids c
            join hr.salary_payments sp on sp.company_id = c.company_id and sp.id = c.salary_payment_id
            join hr.payroll_lines l on l.company_id = sp.company_id and l.id = sp.payroll_line_id
            join party.parties p on p.company_id = sp.company_id and p.id = sp.employee_party_id
            join core.stores s on s.company_id = sp.company_id and s.id = sp.store_id
            left join sec.users paid_by_user on paid_by_user.company_id = sp.company_id and paid_by_user.id = sp.paid_by
            where c.company_id = {0} and c.client_transaction_id = {1}
            """, companyId, clientTransactionId).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : ToPaymentResponse(rows[0]);
    }

    public async Task<IReadOnlyList<SalaryPaymentResponse>> ListPaymentsOfLineAsync(Guid lineId, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var rows = await databaseContext.Database.SqlQueryRaw<PaymentRow>($$"""
            select {{PaymentColumns}}
            from hr.salary_payments sp
            join hr.payroll_lines l on l.company_id = sp.company_id and l.id = sp.payroll_line_id
            join party.parties p on p.company_id = sp.company_id and p.id = sp.employee_party_id
            join core.stores s on s.company_id = sp.company_id and s.id = sp.store_id
            left join sec.users paid_by_user on paid_by_user.company_id = sp.company_id and paid_by_user.id = sp.paid_by
            where sp.company_id = {0} and sp.payroll_line_id = {1}
            order by sp.paid_on, sp.created_at, sp.id
            """, companyId, lineId).ToListAsync(cancellationToken);
        return rows.Select(ToPaymentResponse).ToList();
    }

    public async Task<PagedResult<SalaryPaymentResponse>> SearchPaymentsAsync(SalaryPaymentSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        object?[] filterParameters =
        [
            currentUser.CompanyId, filter.StoreId, filter.EmployeePartyId, filter.FromDate, filter.ToDate,
            pageRequest.SearchText is null ? null : EFootwear.DAL.Common.LikePattern.Contains(pageRequest.SearchText),
            currentUser.HasAccessToAllStores, currentUser.AssignedStoreIds.ToArray(),
        ];

        var totalCount = await databaseContext.Database
            .SqlQueryRaw<long>($"select count(*) as \"Value\" {PaymentFromAndWhere}", filterParameters)
            .SingleAsync(cancellationToken);
        if (totalCount == 0) return PagedResult<SalaryPaymentResponse>.Empty(pageRequest);

        var direction = pageRequest.SortDirection == SortDirection.Descending ? "desc" : "asc";
        var primaryOrder = pageRequest.SortField is { } sortField && PaymentSortableColumns.TryGetValue(sortField, out var column)
            ? $"{column} {direction}, " : "";
        var parameters = filterParameters.Concat(new object?[] { pageRequest.PageSize, pageRequest.SkippedItemCount }).ToArray();

        var rows = await databaseContext.Database.SqlQueryRaw<PaymentRow>($$"""
            select {{PaymentColumns}} {{PaymentFromAndWhere}}
             order by {{primaryOrder}}sp.paid_on desc, sp.created_at desc, sp.id
             limit {8} offset {9}
            """, parameters).ToListAsync(cancellationToken);
        return new PagedResult<SalaryPaymentResponse>(rows.Select(ToPaymentResponse).ToList(), pageRequest.PageNumber, pageRequest.PageSize, totalCount);
    }

    private async Task<PayrollSheetResponse> LoadSheetAsync(SheetHeaderRow header, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var lineRows = await databaseContext.Database.SqlQuery<SheetLineRow>($"""
            select l.id as "LineId", l.employee_party_id as "EmployeePartyId", p.code as "EmployeeCode", p.name as "EmployeeName",
                   sp.designation as "Designation", l.store_id as "StoreId", s.name as "StoreName", l.base_salary as "BaseSalary",
                   l.total_sales as "TotalSales", l.commission_amount as "CommissionAmount", l.incentive_amount as "IncentiveAmount",
                   l.deduction_amount as "DeductionAmount", l.net_pay as "NetPay", l.paid_amount as "PaidAmount",
                   l.outstanding_amount as "OutstandingAmount", l.status as "StatusCode"
              from hr.payroll_lines l
              join party.parties p on p.company_id = l.company_id and p.id = l.employee_party_id
              join core.stores s on s.company_id = l.company_id and s.id = l.store_id
              left join party.staff_profiles sp on sp.company_id = l.company_id and sp.party_id = l.employee_party_id
             where l.company_id = {companyId} and l.payroll_run_id = {header.RunId} and l.status <> 'CANCELLED'
             order by p.name, l.id
            """).ToListAsync(cancellationToken);

        var lines = lineRows.Select(row => new PayrollSheetLineResponse(
            row.LineId, row.EmployeePartyId, row.EmployeeCode, row.EmployeeName, row.Designation, row.StoreId, row.StoreName, row.BaseSalary,
            row.TotalSales, row.CommissionAmount, row.IncentiveAmount, row.DeductionAmount, row.NetPay, row.PaidAmount, row.OutstandingAmount,
            row.StatusCode)).ToList();

        return new PayrollSheetResponse(
            header.RunId, header.StoreId, header.StoreName, header.PeriodMonth, header.StatusCode, header.ApprovedByUserId, header.ApprovedByName,
            header.ApprovedAt, header.RowVersion, lines,
            lines.Sum(line => line.BaseSalary), lines.Sum(line => line.CommissionAmount), lines.Sum(line => line.IncentiveAmount),
            lines.Sum(line => line.DeductionAmount), lines.Sum(line => line.NetPay), lines.Sum(line => line.PaidAmount),
            lines.Sum(line => line.OutstandingAmount));
    }

    private static SalaryPaymentResponse ToPaymentResponse(PaymentRow row) =>
        new(row.Id, row.PayrollLineId, row.EmployeePartyId, row.EmployeeName, row.StoreId, row.StoreName, row.PeriodMonth, row.PaidOn, row.Amount,
            row.MethodCode, row.Reference, row.PaidByUserId, row.PaidByName, row.CreatedAt);

    private sealed class SheetHeaderRow
    {
        public Guid RunId { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public DateOnly PeriodMonth { get; set; }
        public string StatusCode { get; set; } = "";
        public Guid? ApprovedByUserId { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public long RowVersion { get; set; }
    }

    private sealed class SheetLineRow
    {
        public Guid LineId { get; set; }
        public Guid EmployeePartyId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string? Designation { get; set; }
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public long BaseSalary { get; set; }
        public long TotalSales { get; set; }
        public long CommissionAmount { get; set; }
        public long IncentiveAmount { get; set; }
        public long DeductionAmount { get; set; }
        public long NetPay { get; set; }
        public long PaidAmount { get; set; }
        public long OutstandingAmount { get; set; }
        public string StatusCode { get; set; } = "";
    }

    private sealed class AdjustmentRow
    {
        public Guid Id { get; set; }
        public Guid EmployeePartyId { get; set; }
        public string EmployeeName { get; set; } = "";
        public DateOnly PeriodMonth { get; set; }
        public string AdjustmentTypeCode { get; set; } = "";
        public long Amount { get; set; }
        public string? Reason { get; set; }
        public bool IsOnSalarySheet { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class LineSummaryRow
    {
        public Guid LineId { get; set; }
        public Guid StoreId { get; set; }
        public Guid EmployeePartyId { get; set; }
        public DateOnly PeriodMonth { get; set; }
        public string StatusCode { get; set; } = "";
        public long OutstandingAmount { get; set; }
    }

    private sealed class PaymentRow
    {
        public Guid Id { get; set; }
        public Guid PayrollLineId { get; set; }
        public Guid EmployeePartyId { get; set; }
        public string EmployeeName { get; set; } = "";
        public Guid StoreId { get; set; }
        public string StoreName { get; set; } = "";
        public DateOnly PeriodMonth { get; set; }
        public DateOnly PaidOn { get; set; }
        public long Amount { get; set; }
        public string MethodCode { get; set; } = "";
        public string? Reference { get; set; }
        public Guid? PaidByUserId { get; set; }
        public string? PaidByName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
