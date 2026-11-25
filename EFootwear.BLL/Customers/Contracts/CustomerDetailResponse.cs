using EFootwear.BLL.Contacts.Contracts;

namespace EFootwear.BLL.Customers.Contracts;

/// <summary>Everything the New Customer screen shows for one customer, including the opening balance in force. BalanceAmount comes from the journal (whole rupees).</summary>
public sealed record CustomerDetailResponse(
    Guid Id,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    Guid? CityId,
    string? CityName,
    string StatusCode,
    DateTimeOffset CreatedAt,
    long BalanceAmount,
    ContactOpeningBalanceResponse? OpeningBalance,
    long RowVersion);
