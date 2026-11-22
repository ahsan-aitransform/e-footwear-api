namespace EFootwear.Domain.Platform;

/// <summary>platform.admin_actions.action: what a platform administrator did. Mirrors the CHECK constraint (migration 0150).</summary>
public static class AdministratorActionCodes
{
    public const string Login = "LOGIN";
    public const string CompanyCreated = "COMPANY_CREATED";
    public const string StoreCreated = "STORE_CREATED";
    public const string UserCreated = "USER_CREATED";
    public const string PlanCreated = "PLAN_CREATED";
    public const string PlanUpdated = "PLAN_UPDATED";
    public const string CompanyPlanAssigned = "COMPANY_PLAN_ASSIGNED";
    public const string CompanyStatusChanged = "COMPANY_STATUS_CHANGED";

    public static readonly IReadOnlyList<string> All =
    [
        Login, CompanyCreated, StoreCreated, UserCreated, PlanCreated, PlanUpdated, CompanyPlanAssigned, CompanyStatusChanged,
    ];
}
