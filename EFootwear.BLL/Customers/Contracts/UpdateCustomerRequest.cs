using EFootwear.BLL.Contacts.Contracts;

namespace EFootwear.BLL.Customers.Contracts;

/// <summary>Edit a customer. RowVersion is the value the screen loaded; a stale value is refused with 409. A null opening balance leaves it as it is, and so does a null customer type.</summary>
public sealed record UpdateCustomerRequest(
    string Name,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    Guid? CityId,
    string StatusCode,
    ContactOpeningBalanceRequest? OpeningBalance,
    long RowVersion,
    string? Cnic = null,
    string? CustomerTypeCode = null) : ICustomerEditableFields;
