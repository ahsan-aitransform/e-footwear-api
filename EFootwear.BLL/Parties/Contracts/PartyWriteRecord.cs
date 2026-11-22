namespace EFootwear.BLL.Parties.Contracts;

/// <summary>
/// The validated, normalised values of a party that the data layer saves (the same shape for create and update). Built by
/// PartyService after every rule has been checked; the repository trusts it.
/// </summary>
public sealed record PartyWriteRecord(
    string Name,
    string? NameUrdu,
    string? FatherName,
    string? FatherNameUrdu,
    string? Cnic,
    Guid? CityId,
    string? PrimaryPhone,
    string? SecondaryPhone,
    string? Email,
    string? Address,
    string? AddressUrdu,
    string? Notes,
    bool IsCustomer,
    bool IsVendor,
    bool IsStaff,
    long? CreditLimit,
    string StatusCode,
    string? Designation,
    bool IsManager,
    Guid? StaffStoreId);
