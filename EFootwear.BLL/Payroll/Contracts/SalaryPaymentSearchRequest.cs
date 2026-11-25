namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>Filters of the salary payment history: store, employee and a date range of the payment date.</summary>
public sealed record SalaryPaymentSearchRequest(Guid? StoreId, Guid? StaffId, DateOnly? FromDate, DateOnly? ToDate);
