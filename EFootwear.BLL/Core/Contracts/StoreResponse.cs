namespace EFootwear.BLL.Core.Contracts;

public sealed record StoreResponse(Guid Id, string Code, string Name, bool IsHeadOffice, bool IsActive);
