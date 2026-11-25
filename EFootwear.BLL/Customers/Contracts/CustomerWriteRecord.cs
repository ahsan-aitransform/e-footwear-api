namespace EFootwear.BLL.Customers.Contracts;

/// <summary>
/// The validated, normalised values of a customer that the data layer saves (the same shape for create and update). Built by
/// CustomerService after every rule has been checked; the repository trusts it.
/// </summary>
public sealed record CustomerWriteRecord(
    string Name,
    string Phone,
    string? Email,
    DateOnly? BirthDate,
    Guid? CityId,
    string StatusCode);
