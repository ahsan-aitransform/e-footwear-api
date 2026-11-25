using EFootwear.BLL.Contacts.Contracts;

namespace EFootwear.BLL.Vendors.Contracts;

/// <summary>The fields a person can edit on the New Vendor screen. Create and update share them, so the same rules validate both.</summary>
public interface IVendorEditableFields
{
    string Name { get; }
    string? Phone { get; }
    string? Email { get; }
    DateOnly? BirthDate { get; }
    Guid? CityId { get; }
    long? CreditLimit { get; }
    string StatusCode { get; }
    ContactOpeningBalanceRequest? OpeningBalance { get; }
}
