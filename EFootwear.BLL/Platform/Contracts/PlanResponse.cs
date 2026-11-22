namespace EFootwear.BLL.Platform.Contracts;

public sealed record PlanResponse(string Code, string Name, int MaximumStores, int MaximumUsers, bool IsActive, int CompanyCount);
