using EFootwear.BLL.Common;
using EFootwear.BLL.Payroll.Contracts;

namespace EFootwear.BLL.Payroll.Abstractions;

/// <summary>
/// Salary sheets, adjustments and salary payments. Implemented in the DAL. Every write goes through the baseline database functions
/// (hr.build_payroll_run, hr.approve_payroll_run, hr.pay_salary_once): the payroll rules live in the database and are not repeated in C#.
/// </summary>
public interface IPayrollRepository
{
    /// <summary>The sheet of a store and month (all non-cancelled lines), or null when none was built yet.</summary>
    Task<PayrollSheetResponse?> FindSheetAsync(Guid storeId, DateOnly periodMonth, CancellationToken cancellationToken);

    Task<PayrollSheetResponse?> FindSheetByRunIdAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Builds the sheet, or rebuilds it while it is a draft (hr.build_payroll_run). Returns the run id.</summary>
    Task<Guid> BuildSheetAsync(Guid storeId, DateOnly periodMonth, Guid userId, CancellationToken cancellationToken);

    /// <summary>Approves a draft sheet and posts its salary expense (hr.approve_payroll_run). Returns the journal entry id.</summary>
    Task<Guid> ApproveSheetAsync(Guid runId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayrollAdjustmentResponse>> ListAdjustmentsAsync(Guid storeId, DateOnly periodMonth, CancellationToken cancellationToken);

    /// <summary>True when the employee already has an approved or paid line for the month (an adjustment entered now would never reach a sheet).</summary>
    Task<bool> HasApprovedLineAsync(Guid employeePartyId, DateOnly periodMonth, CancellationToken cancellationToken);

    Task<Guid> AddAdjustmentAsync(CreatePayrollAdjustmentRequest request, DateOnly periodMonth, Guid userId, CancellationToken cancellationToken);

    Task<PayrollLineSummary?> FindLineAsync(Guid lineId, CancellationToken cancellationToken);

    /// <summary>Pays salary through hr.pay_salary_once; a repeated client transaction id returns the first payment. Returns the payment id.</summary>
    Task<Guid> PaySalaryAsync(SalaryPaymentWriteRecord payment, CancellationToken cancellationToken);

    Task<SalaryPaymentResponse?> FindPaymentAsync(Guid paymentId, CancellationToken cancellationToken);

    /// <summary>The payment recorded for a client transaction id, or null: a retry is answered with it before any rule that depends on what is still owed is checked.</summary>
    Task<SalaryPaymentResponse?> FindPaymentByClientTransactionAsync(Guid clientTransactionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SalaryPaymentResponse>> ListPaymentsOfLineAsync(Guid lineId, CancellationToken cancellationToken);

    Task<PagedResult<SalaryPaymentResponse>> SearchPaymentsAsync(SalaryPaymentSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken);
}
