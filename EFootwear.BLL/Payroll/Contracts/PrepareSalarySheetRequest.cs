namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>Builds the salary sheet of a store for a month, or rebuilds it while it is still a draft. Any day of the month may be sent; it is moved to the first.</summary>
public sealed record PrepareSalarySheetRequest(Guid StoreId, DateOnly Month);
