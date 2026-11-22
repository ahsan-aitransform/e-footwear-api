namespace EFootwear.BLL.Parties.Contracts;

/// <summary>The fields a person can edit on the New Party screen. Create and update share them, so the same rules validate both.</summary>
public interface IPartyEditableFields
{
    string Name { get; }
    string? NameUrdu { get; }
    string? FatherName { get; }
    string? FatherNameUrdu { get; }
    string? Cnic { get; }
    Guid? CityId { get; }
    string? PrimaryPhone { get; }
    string? SecondaryPhone { get; }
    string? Email { get; }
    string? Address { get; }
    string? AddressUrdu { get; }
    string? Notes { get; }
    bool IsCustomer { get; }
    bool IsVendor { get; }
    bool IsStaff { get; }
    long? CreditLimit { get; }
    string StatusCode { get; }
    string? Designation { get; }
    bool IsManager { get; }
    Guid? StaffStoreId { get; }
    PartyOpeningBalanceRequest? OpeningBalance { get; }
}
