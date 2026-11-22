namespace EFootwear.BLL.Parties.Contracts;

/// <summary>A light party row for a dropdown on another screen (customer on a sale, vendor on a purchase). Any signed-in company user may read it.</summary>
public sealed record PartyLookupItemResponse(Guid Id, string Code, string Name, string? NameUrdu, string? PrimaryPhone,
                                             bool IsCustomer, bool IsVendor, bool IsStaff);
