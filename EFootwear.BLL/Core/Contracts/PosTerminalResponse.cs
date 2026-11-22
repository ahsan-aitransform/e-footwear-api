namespace EFootwear.BLL.Core.Contracts;

public sealed record PosTerminalResponse(Guid Id, Guid StoreId, string StoreCode, string? Label, bool IsActive, DateTimeOffset? LastSeenAt);
