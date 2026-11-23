namespace EFootwear.BLL.HumanResources.Contracts;

/// <summary>One entry of a staff member's salary history (<c>hr.salary_terms</c>): the base salary and pay frequency in force from a date, and who changed it.</summary>
public sealed record SalaryTermResponse(
    Guid Id,
    DateOnly EffectiveFrom,
    long BaseSalary,
    string PayFrequencyCode,
    Guid? ChangedByUserId,
    string? ChangedByName,
    DateTimeOffset ChangedAt);
