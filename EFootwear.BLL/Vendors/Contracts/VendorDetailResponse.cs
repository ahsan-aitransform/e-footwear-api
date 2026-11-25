using EFootwear.BLL.Contacts.Contracts;

namespace EFootwear.BLL.Vendors.Contracts;

/// <summary>Everything the New Vendor screen shows for one vendor, including the opening balance in force. BalanceAmount comes from the journal (whole rupees).</summary>
public sealed record VendorDetailResponse(
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
    DateTimeOffset CreatedAt,
    long BalanceAmount,
    ContactOpeningBalanceResponse? OpeningBalance,
    long RowVersion);
