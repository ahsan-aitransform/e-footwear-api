namespace EFootwear.Domain.Security;

/// <summary>Role codes that exist in sec.roles. Use these constants instead of typing strings.</summary>
public static class RoleCodes
{
    /// <summary>Platform level. Creates companies, company owners, store managers and stores. Not tied to a company.</summary>
    public const string PlatformAdmin = "PLATFORM_ADMIN";

    /// <summary>Company level. Sees all stores of its company.</summary>
    public const string CompanyOwner = "COMPANY_OWNER";

    /// <summary>Store level. Head of one store. Adds staff to that store only.</summary>
    public const string StoreManager = "STORE_MANAGER";

    public const string CashierSalesman = "CASHIER_SALESMAN";
    public const string Accountant = "ACCOUNTANT";
}
