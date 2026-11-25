namespace EFootwear.BLL.Staff.Contracts;

/// <summary>The validated, normalised values of a staff member that the data layer saves (the same shape for create and update). Built by StaffMemberService.</summary>
public sealed record StaffMemberWriteRecord(
    string Name,
    string Phone,
    string? Email,
    DateOnly? BirthDate,
    Guid? CityId,
    string? Designation,
    bool IsManager,
    string StatusCode);
