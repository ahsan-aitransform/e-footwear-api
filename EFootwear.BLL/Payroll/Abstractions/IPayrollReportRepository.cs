using EFootwear.BLL.Payroll.Contracts;

namespace EFootwear.BLL.Payroll.Abstractions;

/// <summary>The Staff Salary and Salary Outstanding reports. Plain reads over the salary sheet tables and hr.v_salary_outstanding; row-level security limits them to the caller's stores.</summary>
public interface IPayrollReportRepository
{
    Task<StaffSalaryReportResponse> GetStaffSalaryReportAsync(StaffSalaryReportRequest request, int rowLimit, CancellationToken cancellationToken);

    Task<SalaryOutstandingReportResponse> GetSalaryOutstandingReportAsync(SalaryOutstandingReportRequest request, int rowLimit, CancellationToken cancellationToken);
}
