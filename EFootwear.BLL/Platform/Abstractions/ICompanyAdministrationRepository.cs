namespace EFootwear.BLL.Platform.Abstractions;

/// <summary>Platform-level changes to a company: its plan and its status. Both run in database functions that check the administrator.</summary>
public interface ICompanyAdministrationRepository
{
    Task AssignPlanAsync(Guid administratorId, Guid companyId, string planCode, CancellationToken cancellationToken);

    Task SetStatusAsync(Guid administratorId, Guid companyId, string status, CancellationToken cancellationToken);
}
