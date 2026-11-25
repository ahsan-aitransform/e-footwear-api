using EFootwear.BLL.Contacts.Contracts;

namespace EFootwear.BLL.Staff.Contracts;

/// <summary>Everything the New Staff screen shows for one staff member, including the store and the opening balance in force. BalanceAmount comes from the journal (whole rupees).</summary>
public sealed record StaffMemberDetailResponse(
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
    DateOnly? JoinedOn,
    DateOnly? LeftOn,
    string StatusCode,
    DateTimeOffset CreatedAt,
    long BalanceAmount,
    ContactOpeningBalanceResponse? OpeningBalance,
    long RowVersion);
