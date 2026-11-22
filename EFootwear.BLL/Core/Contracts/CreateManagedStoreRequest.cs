namespace EFootwear.BLL.Core.Contracts;

public sealed record CreateManagedStoreRequest(string Code, string Name, string? Address, string? Phone);
