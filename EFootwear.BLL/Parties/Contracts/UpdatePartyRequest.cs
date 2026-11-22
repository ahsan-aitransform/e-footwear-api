namespace EFootwear.BLL.Parties.Contracts;

/// <summary>Edit a party. RowVersion is the value the screen loaded; a stale value is refused with 409. A null opening balance leaves it as it is.</summary>
public sealed record UpdatePartyRequest(
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
    Guid? StaffStoreId,
    PartyOpeningBalanceRequest? OpeningBalance,
    long RowVersion) : IPartyEditableFields;
