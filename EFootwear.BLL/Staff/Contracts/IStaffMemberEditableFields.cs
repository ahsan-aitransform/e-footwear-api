using EFootwear.BLL.Contacts.Contracts;

namespace EFootwear.BLL.Staff.Contracts;

/// <summary>The fields a person can edit on the New Staff screen. Create and update share them, so the same rules validate both.</summary>
public interface IStaffMemberEditableFields
{
    string Name { get; }
    string? Phone { get; }
    string? Email { get; }
    DateOnly? BirthDate { get; }
    Guid? CityId { get; }
    string? Designation { get; }
    bool IsManager { get; }
    string StatusCode { get; }
    ContactOpeningBalanceRequest? OpeningBalance { get; }
}
