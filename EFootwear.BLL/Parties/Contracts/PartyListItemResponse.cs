namespace EFootwear.BLL.Parties.Contracts;

/// <summary>
/// A row of Manage Party / Search Party. BalanceAmount is derived from the journal (never stored on the party): positive means the
/// party owes the company (customer), negative means the company owes the party (vendor). Whole rupees.
/// </summary>
public sealed record PartyListItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? NameUrdu,
    string? FatherName,
    string? Cnic,
    string? Address,
    Guid? CityId,
    string? CityName,
    string? PrimaryPhone,
    string? SecondaryPhone,
    string? Designation,
    bool IsCustomer,
    bool IsVendor,
    bool IsStaff,
    string StatusCode,
    long BalanceAmount,
    long RowVersion);
