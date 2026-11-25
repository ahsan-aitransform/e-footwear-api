using EFootwear.Domain.Common;

namespace EFootwear.Domain.Vendors;

/// <summary>Rules of the vendor screens: the credit limit is whole rupees and never negative.</summary>
public static class VendorRules
{
    public static void EnsureCreditLimitIsValid(long? creditLimit)
    {
        if (creditLimit is < 0) throw new DomainException(ErrorKind.Validation, "The credit limit cannot be negative.");
    }
}
