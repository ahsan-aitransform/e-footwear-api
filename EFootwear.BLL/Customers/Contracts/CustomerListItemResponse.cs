namespace EFootwear.BLL.Customers.Contracts;

/// <summary>
/// A row of View Customers / Search Customer. BalanceAmount is derived from the journal (never stored on the customer): positive means the customer owes the company. Whole rupees.
/// </summary>
public sealed record CustomerListItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    DateOnly? BirthDate,
    Guid? CityId,
    string? CityName,
    string StatusCode,
    long BalanceAmount,
    long RowVersion);
