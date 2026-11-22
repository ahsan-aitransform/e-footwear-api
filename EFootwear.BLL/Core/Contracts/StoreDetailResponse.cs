namespace EFootwear.BLL.Core.Contracts;

public sealed record StoreDetailResponse(
    Guid Id,
    string Code,
    string Name,
    bool IsHeadOffice,
    bool IsActive,
    string? Address,
    string? Phone,
    long RowVersion);
