using EFootwear.BLL.Core.Services;
using EFootwear.BLL.Payroll.Abstractions;
using EFootwear.BLL.Payroll.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Payroll;

namespace EFootwear.BLL.Payroll.Services;

/// <summary>
/// The Staff Salary and Salary Outstanding reports (Part 22 additional reports). Both read salary sheet lines that are not cancelled, so a rebuilt draft
/// never counts twice. The store filter is checked against the caller's stores; with no store the database limits the answer to the stores they may see.
/// </summary>
public sealed class PayrollReportService(IPayrollReportRepository payrollReportRepository, StoreAccessGuard storeAccessGuard)
{
    public async Task<StaffSalaryReportResponse> GetStaffSalaryReportAsync(StaffSalaryReportRequest request, CancellationToken cancellationToken)
    {
        var fromMonth = PayrollRules.NormalizeToFirstDayOfMonth(request.FromMonth);
        var toMonth = PayrollRules.NormalizeToFirstDayOfMonth(request.ToMonth);
        if (fromMonth > toMonth)
            throw new DomainException(ErrorKind.Validation, "The first month must not be after the last month.");
        if (request.StoreId is { } storeId) await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);

        return await payrollReportRepository.GetStaffSalaryReportAsync(
            request with { FromMonth = fromMonth, ToMonth = toMonth }, PayrollRules.ReportRowLimit, cancellationToken);
    }

    public async Task<SalaryOutstandingReportResponse> GetSalaryOutstandingReportAsync(SalaryOutstandingReportRequest request, CancellationToken cancellationToken)
    {
        if (request.StoreId is { } storeId) await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        return await payrollReportRepository.GetSalaryOutstandingReportAsync(request, PayrollRules.ReportRowLimit, cancellationToken);
    }
}
