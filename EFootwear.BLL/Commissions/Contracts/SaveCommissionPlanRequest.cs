namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>
/// Creates or replaces the settings of a commission plan. <see cref="MemberStaffIds"/> limits the plan to those staff (one id = a plan for one person,
/// for example a salesman who earns 3 percent); empty means every staff member of the role. <see cref="RowVersion"/> is required when changing a plan,
/// so two people editing the same plan cannot overwrite each other.
/// </summary>
public sealed record SaveCommissionPlanRequest(
    string Name,
    string AppliesToCode,
    string BasisCode,
    string CalculationTypeCode,
    decimal? DefaultRate,
    long? DefaultFixedAmountPerPiece,
    long MinimumStoreSales,
    Guid? StoreId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    bool IsActive,
    IReadOnlyList<Guid>? MemberStaffIds,
    long? RowVersion);
