namespace EFootwear.BLL.Vendors.Contracts;

/// <summary>
/// A row of View Vendors. BalanceAmount is derived from the journal (never stored on the vendor): negative means the company owes the vendor. Whole rupees.
/// </summary>
public sealed record VendorListItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    Guid? CityId,
    string? CityName,
    long? CreditLimit,
    string StatusCode,
    long BalanceAmount,
    long RowVersion);
