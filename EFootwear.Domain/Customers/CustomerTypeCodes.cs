namespace EFootwear.Domain.Customers;

/// <summary>The type of a customer (dropdown Regular / Special). The values mirror the CHECK constraint of contacts.customers.</summary>
public static class CustomerTypeCodes
{
    public const string Regular = "REGULAR";
    public const string Special = "SPECIAL";

    public static bool IsValid(string? customerTypeCode) => customerTypeCode is Regular or Special;
}
