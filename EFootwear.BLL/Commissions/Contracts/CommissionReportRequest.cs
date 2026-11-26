namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>Filters of the commission reports (docx 8.14): a store (empty = every store the person may see), one salesman or all, and a range of sale dates.</summary>
public sealed record CommissionReportRequest(Guid? StoreId, Guid? StaffId, DateOnly FromDate, DateOnly ToDate);
