namespace EFootwear.BLL.HumanResources.Contracts;

/// <summary>New base salary (whole rupees) and pay frequency for a staff member. The database adds the change to the salary history with today's date.</summary>
public sealed record UpdateStaffSalaryTermsRequest(long BaseSalary, string PayFrequencyCode, long RowVersion);
