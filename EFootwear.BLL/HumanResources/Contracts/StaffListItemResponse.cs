namespace EFootwear.BLL.HumanResources.Contracts;

/// <summary>One staff member as the staff list and the staff page show it: the party plus the job details of <c>party.staff_profiles</c>. Money is whole rupees.</summary>
public sealed record StaffListItemResponse(
    Guid PartyId,
    string Code,
    string Name,
    string? Designation,
    bool IsManager,
    Guid CurrentStoreId,
    string CurrentStoreName,
    long BaseSalary,
    string PayFrequencyCode,
    DateOnly? JoinedOn,
    DateOnly? LeftOn,
    string PartyStatusCode);
