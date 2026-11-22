namespace EFootwear.BLL.Platform.Contracts;

public sealed record CreatePlanRequest(string Code, string Name, int MaximumStores, int MaximumUsers);
