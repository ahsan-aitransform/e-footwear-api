using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Platform;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Platform.Services;

/// <summary>Subscription plans: the platform owner defines plans and their limits. Platform administrators only.</summary>
public sealed class PlanAdministrationService(
    IPlanRepository planRepository,
    IPlatformRepository platformRepository,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<PlanAdministrationService> logger)
{
    public async Task<IReadOnlyList<PlanResponse>> ListAsync(CancellationToken cancellationToken)
    {
        RequirePlatformAdministrator();
        return await planRepository.ListAsync(cancellationToken);
    }

    public async Task CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        RequirePlatformAdministrator();
        var code = PlanRules.EnsureValidCode(request.Code);
        var name = PlanRules.EnsureValidName(request.Name);
        PlanRules.EnsureValidLimits(request.MaximumStores, request.MaximumUsers);
        if (await planRepository.FindAsync(code, cancellationToken) is not null)
            throw new DomainException(ErrorKind.Conflict, $"The plan {code} already exists.");

        var administratorId = requestContext.PlatformAdministratorId!.Value;
        await planRepository.CreateAsync(administratorId, code, name, request.MaximumStores, request.MaximumUsers, cancellationToken);
        await platformRepository.LogAdministratorActionAsync(administratorId, AdministratorActionCodes.PlanCreated, null,
            new { code, name, request.MaximumStores, request.MaximumUsers }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Plan {PlanCode} created by platform admin {AdministratorId}", code, administratorId);
    }

    public async Task UpdateAsync(string planCode, UpdatePlanRequest request, CancellationToken cancellationToken)
    {
        RequirePlatformAdministrator();
        var code = PlanRules.EnsureValidCode(planCode);
        var name = PlanRules.EnsureValidName(request.Name);
        PlanRules.EnsureValidLimits(request.MaximumStores, request.MaximumUsers);
        var existing = await planRepository.FindAsync(code, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "Plan not found.");

        var administratorId = requestContext.PlatformAdministratorId!.Value;
        await planRepository.UpdateAsync(administratorId, code, name, request.MaximumStores, request.MaximumUsers, request.IsActive, cancellationToken);
        await platformRepository.LogAdministratorActionAsync(administratorId, AdministratorActionCodes.PlanUpdated, null, new
        {
            code,
            before = new { existing.Name, existing.MaximumStores, existing.MaximumUsers, existing.IsActive },
            after = new { name, request.MaximumStores, request.MaximumUsers, request.IsActive },
        }, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Plan {PlanCode} updated by platform admin {AdministratorId}", code, administratorId);
    }

    private void RequirePlatformAdministrator()
    {
        if (!requestContext.IsPlatformAdministrator)
            throw new DomainException(ErrorKind.Forbidden, "Only platform administrators can do this.");
    }
}
