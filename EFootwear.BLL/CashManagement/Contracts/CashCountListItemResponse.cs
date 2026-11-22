namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>A row of View Cash Detail (docx 3.7 grid: Computer Cash, Original Cash, Difference, User, Date, Time). The time is part of CreatedAt.</summary>
public sealed record CashCountListItemResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    DateOnly CountDate,
    long ExpectedCash,
    long CountedCash,
    long DifferenceAmount,
    string? CountedByName,
    DateTimeOffset CreatedAt);
