namespace EFootwear.Domain.Inventory;

/// <summary>The document types of inv.documents (column doc_type). A claim has its own table (inv.claims); the value exists because the stock ledger uses it.</summary>
public static class InventoryDocumentTypeCodes
{
    public const string Adjustment = "ADJUSTMENT";
    public const string Audit = "AUDIT";
    public const string Claim = "CLAIM";

    public static bool IsValid(string? code) => code is Adjustment or Audit or Claim;
}
