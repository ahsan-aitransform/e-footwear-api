namespace EFootwear.BLL.Staff.Contracts;

/// <summary>A row of View Staff / Search Staff. BalanceAmount is derived from the journal (never stored on the staff member). Whole rupees.</summary>
public sealed record StaffMemberListItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    Guid? CityId,
    string? CityName,
    string? Designation,
    bool IsManager,
    Guid StoreId,
    string? StoreName,
    string StatusCode,
    long BalanceAmount,
    long RowVersion);
