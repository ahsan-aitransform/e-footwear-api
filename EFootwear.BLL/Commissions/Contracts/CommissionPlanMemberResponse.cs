namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>A staff member a commission plan is limited to. A plan with no members pays every staff member of the role it applies to.</summary>
public sealed record CommissionPlanMemberResponse(Guid StaffId, string StaffCode, string StaffName);
