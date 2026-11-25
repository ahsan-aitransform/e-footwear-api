using EFootwear.BLL.Contacts.Contracts;

namespace EFootwear.BLL.Staff.Contracts;

/// <summary>
/// Edit a staff member. RowVersion is the value the screen loaded; a stale value is refused with 409. The store of an existing staff member
/// only changes through a transfer (which leaves a history row), so it is not part of this request. A null opening balance leaves it as it is.
/// </summary>
public sealed record UpdateStaffMemberRequest(
    string Name,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    Guid? CityId,
    string? Designation,
    bool IsManager,
    string StatusCode,
    ContactOpeningBalanceRequest? OpeningBalance,
    long RowVersion) : IStaffMemberEditableFields;
