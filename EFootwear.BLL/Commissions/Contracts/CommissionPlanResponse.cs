namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>
/// A commission plan (Part 16.2): who it pays, what it is based on, the default rate or fixed amount, the stores and dates it is in force for,
/// the staff it is limited to and its rules. The list endpoint leaves <see cref="Members"/> and <see cref="Rules"/> empty and fills the counts.
/// </summary>
public sealed record CommissionPlanResponse(
    Guid Id,
    string Name,
    string AppliesToCode,
    string BasisCode,
    string CalculationTypeCode,
    decimal? DefaultRate,
    long? DefaultFixedAmountPerPiece,
    long MinimumStoreSales,
    Guid? StoreId,
    string? StoreName,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    bool IsActive,
    int MemberCount,
    int RuleCount,
    IReadOnlyList<CommissionPlanMemberResponse> Members,
    IReadOnlyList<CommissionRuleResponse> Rules,
    long RowVersion);
