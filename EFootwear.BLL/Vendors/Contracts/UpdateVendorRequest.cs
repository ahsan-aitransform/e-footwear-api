using EFootwear.BLL.Contacts.Contracts;

namespace EFootwear.BLL.Vendors.Contracts;

/// <summary>Edit a vendor. RowVersion is the value the screen loaded; a stale value is refused with 409. A null opening balance leaves it as it is.</summary>
public sealed record UpdateVendorRequest(
    string Name,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    Guid? CityId,
    long? CreditLimit,
    string StatusCode,
    ContactOpeningBalanceRequest? OpeningBalance,
    long RowVersion) : IVendorEditableFields;
