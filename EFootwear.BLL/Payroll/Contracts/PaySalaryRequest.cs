namespace EFootwear.BLL.Payroll.Contracts;

/// <summary>
/// Pays all or part of an approved salary line. <paramref name="ClientTransactionId"/> makes a retry safe: a repeated id returns the first payment and never
/// pays twice (Part 26).
/// </summary>
public sealed record PaySalaryRequest(long Amount, string MethodCode, DateOnly PaidOn, string? Reference, Guid? ClientTransactionId);
