using EFootwear.Domain.Common;

namespace EFootwear.Domain.Contacts;

/// <summary>
/// Names one customer, vendor or staff member: the kind and the id. Contracts that carry three optional ids (customerId, vendorId,
/// staffId) turn them into a reference with <see cref="FromOptionalIds"/>; at most one id may be present.
/// </summary>
public sealed record ContactReference(string KindCode, Guid Id)
{
    public static ContactReference? FromOptionalIds(Guid? customerId, Guid? vendorId, Guid? staffId, string what)
    {
        var present = new[] { customerId.HasValue, vendorId.HasValue, staffId.HasValue }.Count(isPresent => isPresent);
        if (present > 1)
            throw new DomainException(ErrorKind.Validation, $"Name only one of customer, vendor or staff member for {what}.");
        if (customerId is { } customer) return Of(ContactKindCodes.Customer, customer, what);
        if (vendorId is { } vendor) return Of(ContactKindCodes.Vendor, vendor, what);
        if (staffId is { } staff) return Of(ContactKindCodes.Staff, staff, what);
        return null;
    }

    private static ContactReference Of(string kindCode, Guid id, string what) =>
        id == Guid.Empty ? throw new DomainException(ErrorKind.Validation, $"The choice for {what} is not valid.") : new ContactReference(kindCode, id);
}
