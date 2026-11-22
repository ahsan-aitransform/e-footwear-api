using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Platform;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Platform.Services;

/// <summary>A company as the platform sees it: its profile and plan usage, its plan assignment (subscription) and its status.</summary>
public sealed class CompanyAdministrationService(
    ICompanyProfileRepository companyProfileRepository,
    IPlanRepository planRepository,
    ICompanyAdministrationRepository companyAdministrationRepository,
    IPlatformRepository platformRepository,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<CompanyAdministrationService> logger)
{
    private static readonly string[] AllowedStatuses = [CompanyStatusCodes.Active, CompanyStatusCodes.Suspended, CompanyStatusCodes.Closed];

    public async Task<CompanyDetailResponse> GetDetailAsync(Guid companyId, CancellationToken cancellationToken)
    {
        RequirePlatformAdministrator();
        var profile = await companyProfileRepository.FindAsync(companyId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Company not found.");
        var usage = await planRepository.GetUsageForCompanyAsync(companyId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Company not found.");
        return new CompanyDetailResponse(profile, usage);
    }

    public async Task AssignPlanAsync(Guid companyId, AssignCompanyPlanRequest request, CancellationToken cancellationToken)
    {
        RequirePlatformAdministrator();
        var planCode = PlanRules.EnsureValidCode(request.PlanCode);
        var profile = await companyProfileRepository.FindAsync(companyId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Company not found.");
        var plan = await planRepository.FindAsync(planCode, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Plan not found.");
        if (!plan.IsActive)
            throw new DomainException(ErrorKind.Validation, $"The plan {planCode} is retired and cannot be assigned.");

        var administratorId = requestContext.PlatformAdministratorId!.Value;
        await companyAdministrationRepository.AssignPlanAsync(administratorId, companyId, planCode, cancellationToken);
        await platformRepository.LogAdministratorActionAsync(administratorId, AdministratorActionCodes.CompanyPlanAssigned, companyId,
            new { from = profile.PlanCode, to = planCode }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Company {CompanyId} moved from plan {FromPlan} to {ToPlan} by platform admin {AdministratorId}",
            companyId, profile.PlanCode, planCode, administratorId);
    }

    public async Task SetStatusAsync(Guid companyId, SetCompanyStatusRequest request, CancellationToken cancellationToken)
    {
        RequirePlatformAdministrator();
        var status = (request.Status ?? "").Trim().ToUpperInvariant();
        if (!AllowedStatuses.Contains(status))
            throw new DomainException(ErrorKind.Validation, $"Status must be one of: {string.Join(", ", AllowedStatuses)}.");
        var reason = request.Reason?.Trim();
        if (reason is { Length: > 500 })
            throw new DomainException(ErrorKind.Validation, "The reason can have at most 500 characters.");
        var profile = await companyProfileRepository.FindAsync(companyId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Company not found.");

        var administratorId = requestContext.PlatformAdministratorId!.Value;
        await companyAdministrationRepository.SetStatusAsync(administratorId, companyId, status, cancellationToken);
        await platformRepository.LogAdministratorActionAsync(administratorId, AdministratorActionCodes.CompanyStatusChanged, companyId,
            new { from = profile.Status, to = status, reason }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Company {CompanyId} status changed from {FromStatus} to {ToStatus} by platform admin {AdministratorId}",
            companyId, profile.Status, status, administratorId);
    }

    private void RequirePlatformAdministrator()
    {
        if (!requestContext.IsPlatformAdministrator)
            throw new DomainException(ErrorKind.Forbidden, "Only platform administrators can do this.");
    }
}
