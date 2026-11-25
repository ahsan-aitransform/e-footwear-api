namespace EFootwear.Domain.Accounting;

/// <summary>
/// The scope of accounting master data inside a company. The head office is the store with the nil id (schema decision, Part 5.4):
/// company-level accounts and every customer, vendor and staff opening balance belong to it.
/// </summary>
public static class AccountingScope
{
    public static readonly Guid HeadOfficeStoreId = Guid.Empty;
}
