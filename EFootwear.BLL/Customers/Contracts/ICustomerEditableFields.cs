using EFootwear.BLL.Contacts.Contracts;

namespace EFootwear.BLL.Customers.Contracts;

/// <summary>The fields a person can edit on the New Customer screen. Create and update share them, so the same rules validate both.</summary>
public interface ICustomerEditableFields
{
    string Name { get; }
    string? Phone { get; }
    string? Email { get; }
    DateOnly? BirthDate { get; }
    Guid? CityId { get; }
    string StatusCode { get; }
    ContactOpeningBalanceRequest? OpeningBalance { get; }
}
