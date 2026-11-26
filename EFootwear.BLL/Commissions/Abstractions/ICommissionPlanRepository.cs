using EFootwear.BLL.Commissions.Contracts;

namespace EFootwear.BLL.Commissions.Abstractions;

/// <summary>
/// Commission plans, their members and their rules (hr.commission_plans, hr.commission_plan_members, hr.commission_rules). Implemented in the DAL.
/// The commission itself is worked out by the database when a sale is posted (hr.accrue_line_commission), so this repository only keeps the settings it reads.
/// </summary>
public interface ICommissionPlanRepository
{
    /// <summary>Every plan that is not deleted, newest first. Members and rules are not loaded; the counts are.</summary>
    Task<IReadOnlyList<CommissionPlanResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One plan with its members and rules, or null.</summary>
    Task<CommissionPlanResponse?> FindAsync(Guid planId, CancellationToken cancellationToken);

    /// <summary>How many of the given ids are staff of the caller's company that are not deleted.</summary>
    Task<int> CountExistingStaffAsync(IReadOnlyCollection<Guid> staffIds, CancellationToken cancellationToken);

    /// <summary>Creates the plan and its members. Returns the new id.</summary>
    Task<Guid> CreateAsync(SaveCommissionPlanRequest request, Guid userId, CancellationToken cancellationToken);

    /// <summary>Replaces the settings and the members of the plan when its row version is still the one given. False means no such plan with that version.</summary>
    Task<bool> UpdateAsync(Guid planId, SaveCommissionPlanRequest request, long expectedRowVersion, Guid userId, CancellationToken cancellationToken);

    /// <summary>Marks the plan deleted. Entries it already earned keep pointing at it, so history stays readable. False means no such plan.</summary>
    Task<bool> DeleteAsync(Guid planId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Adds a rule. Returns the new id, or null when another rule of the plan already covers the same product, category or price band in those dates.</summary>
    Task<Guid?> AddRuleAsync(Guid planId, SaveCommissionRuleRequest request, Guid userId, CancellationToken cancellationToken);

    /// <summary>Sets the last day of a rule that has none yet or ends later. False means no such rule of that plan.</summary>
    Task<bool> EndRuleAsync(Guid planId, Guid ruleId, DateOnly validTo, Guid userId, CancellationToken cancellationToken);

    /// <summary>The first day of a rule, or null when the plan has no such rule.</summary>
    Task<DateOnly?> FindRuleFirstDayAsync(Guid planId, Guid ruleId, CancellationToken cancellationToken);
}
