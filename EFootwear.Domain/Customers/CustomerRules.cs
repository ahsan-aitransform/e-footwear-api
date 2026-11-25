namespace EFootwear.Domain.Customers;

/// <summary>Facts about customers: the walk-in customer is created by the database for every company and has no phone number.</summary>
public static class CustomerRules
{
    public const string WalkInCustomerCode = "WALKIN";
}
