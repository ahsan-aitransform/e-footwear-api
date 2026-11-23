namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>A salary payment that passed the checks of the use case and is ready to be recorded by the database function (whole rupees).</summary>
public sealed record SalaryPaymentWriteRecord(
    Guid LineId,
    long Amount,
    string MethodCode,
    DateOnly PaidOn,
    string? Reference,
    Guid? ClientTransactionId,
    Guid UserId);
