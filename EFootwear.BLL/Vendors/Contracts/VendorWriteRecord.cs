namespace EFootwear.BLL.Vendors.Contracts;

/// <summary>
/// The validated, normalised values of a vendor that the data layer saves (the same shape for create and update). Built by
/// VendorService after every rule has been checked; the repository trusts it.
/// </summary>
public sealed record VendorWriteRecord(
    string Name,
    string Phone,
    string? Email,
    DateOnly? BirthDate,
    Guid? CityId,
    long? CreditLimit,
    string StatusCode);
