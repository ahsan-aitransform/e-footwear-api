using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Commissions.Abstractions;
using EFootwear.BLL.Commissions.Contracts;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Services;
using EFootwear.Domain.Commissions;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Commissions.Services;

/// <summary>
/// Commission plans, their members and their rules (Part 16.2). A plan says who earns commission, on what, at which rate and from when to when; a plan
/// limited to one staff member is how "this salesman gets 3 percent" is set. The commission is then worked out by the database every time a sale line is
/// posted (hr.accrue_line_commission), so a change of a plan never rewrites commission that was already earned: the entry keeps the rate of that day.
/// A plan of one store is changed by people who work in that store; a plan of every store only by people who may work in every store.
/// </summary>
public sealed class CommissionPlanService(
    ICommissionPlanRepository commissionPlanRepository,
    StoreAccessGuard storeAccessGuard,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<CommissionPlanService> logger)
{
    public Task<IReadOnlyList<CommissionPlanResponse>> ListAsync(CancellationToken cancellationToken) =>
        commissionPlanRepository.ListAsync(cancellationToken);

    public async Task<CommissionPlanResponse> GetAsync(Guid planId, CancellationToken cancellationToken) =>
        await commissionPlanRepository.FindAsync(planId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The commission plan was not found.");

    public async Task<CommissionPlanResponse> CreateAsync(SaveCommissionPlanRequest request, CancellationToken cancellationToken)
    {
        var cleaned = await CleanAndCheckAsync(request, cancellationToken);
        await EnsureMayChangePlansOfStoreAsync(cleaned.StoreId, cancellationToken);

        var planId = await commissionPlanRepository.CreateAsync(cleaned, currentUser.UserId, cancellationToken);
        var plan = await commissionPlanRepository.FindAsync(planId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The commission plan could not be read after it was saved.");
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Commission plan {PlanId} created by user {UserId}", planId, currentUser.UserId);
        return plan;
    }

    public async Task<CommissionPlanResponse> UpdateAsync(Guid planId, SaveCommissionPlanRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVersion is not { } expectedRowVersion)
            throw new DomainException(ErrorKind.Validation, "Open the plan again before saving it: its version is missing.");
        var existing = await GetAsync(planId, cancellationToken);
        await EnsureMayChangePlansOfStoreAsync(existing.StoreId, cancellationToken);

        var cleaned = await CleanAndCheckAsync(request, cancellationToken);
        await EnsureMayChangePlansOfStoreAsync(cleaned.StoreId, cancellationToken);

        var updated = await commissionPlanRepository.UpdateAsync(planId, cleaned, expectedRowVersion, currentUser.UserId, cancellationToken);
        if (!updated)
            throw new DomainException(ErrorKind.Conflict, "Somebody else changed this plan. Open it again and repeat your change.");
        var plan = await GetAsync(planId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Commission plan {PlanId} changed by user {UserId}", planId, currentUser.UserId);
        return plan;
    }

    public async Task DeleteAsync(Guid planId, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(planId, cancellationToken);
        await EnsureMayChangePlansOfStoreAsync(existing.StoreId, cancellationToken);
        if (!await commissionPlanRepository.DeleteAsync(planId, currentUser.UserId, cancellationToken))
            throw new DomainException(ErrorKind.NotFound, "The commission plan was not found.");
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Commission plan {PlanId} deleted by user {UserId}", planId, currentUser.UserId);
    }

    public async Task<CommissionPlanResponse> AddRuleAsync(Guid planId, SaveCommissionRuleRequest request, CancellationToken cancellationToken)
    {
        var plan = await GetAsync(planId, cancellationToken);
        await EnsureMayChangePlansOfStoreAsync(plan.StoreId, cancellationToken);
        CommissionRules.EnsureRuleIsValid(request.ScopeCode, request.ProductId, request.CategoryId, request.PriceBandId,
                                          request.MinimumLineAmount, request.ValidFrom, request.ValidTo);
        CommissionRules.EnsureCalculationIsValid(request.CalculationTypeCode, request.Rate, request.FixedAmountPerPiece);

        var ruleId = await commissionPlanRepository.AddRuleAsync(planId, request, currentUser.UserId, cancellationToken);
        if (ruleId is null)
            throw new DomainException(ErrorKind.Conflict, "Another rule of this plan already covers that product, category or price band on some of those dates. End it first.");
        var changedPlan = await GetAsync(planId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Rule {RuleId} added to commission plan {PlanId} by user {UserId}", ruleId, planId, currentUser.UserId);
        return changedPlan;
    }

    public async Task<CommissionPlanResponse> EndRuleAsync(Guid planId, Guid ruleId, EndCommissionRuleRequest request, CancellationToken cancellationToken)
    {
        var plan = await GetAsync(planId, cancellationToken);
        await EnsureMayChangePlansOfStoreAsync(plan.StoreId, cancellationToken);
        var firstDay = await commissionPlanRepository.FindRuleFirstDayAsync(planId, ruleId, cancellationToken)
            ?? throw new DomainException(ErrorKind.NotFound, "The rule was not found in this plan.");
        if (request.ValidTo < firstDay)
            throw new DomainException(ErrorKind.Validation, "The last day of the rule cannot be before its first day.");

        if (!await commissionPlanRepository.EndRuleAsync(planId, ruleId, request.ValidTo, currentUser.UserId, cancellationToken))
            throw new DomainException(ErrorKind.Conflict, "This rule already ends on or before that day.");
        var changedPlan = await GetAsync(planId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Rule {RuleId} of commission plan {PlanId} ended by user {UserId}", ruleId, planId, currentUser.UserId);
        return changedPlan;
    }

    private async Task<SaveCommissionPlanRequest> CleanAndCheckAsync(SaveCommissionPlanRequest request, CancellationToken cancellationToken)
    {
        CommissionRules.EnsurePlanIsValid(request.Name, request.AppliesToCode, request.BasisCode, request.MinimumStoreSales, request.ValidFrom, request.ValidTo);
        CommissionRules.EnsureCalculationIsValid(request.CalculationTypeCode, request.DefaultRate, request.DefaultFixedAmountPerPiece);

        var memberIds = (request.MemberStaffIds ?? []).Distinct().ToList();
        if (memberIds.Count > 0 && await commissionPlanRepository.CountExistingStaffAsync(memberIds, cancellationToken) != memberIds.Count)
            throw new DomainException(ErrorKind.Validation, "One of the chosen staff members does not exist.");
        if (request.StoreId is { } storeId) await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);

        return request with { Name = request.Name.Trim(), MemberStaffIds = memberIds };
    }

    private Task EnsureMayChangePlansOfStoreAsync(Guid? storeId, CancellationToken cancellationToken)
    {
        if (storeId is { } oneStore)
            return storeAccessGuard.EnsureAccessibleAsync(oneStore, cancellationToken);
        if (!currentUser.HasAccessToAllStores)
            throw new DomainException(ErrorKind.Forbidden, "A plan for every store can only be changed by somebody who may work in every store.");
        return Task.CompletedTask;
    }
}
