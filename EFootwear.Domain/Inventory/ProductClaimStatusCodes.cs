namespace EFootwear.Domain.Inventory;

/// <summary>The statuses of inv.claims (column status).</summary>
public static class ProductClaimStatusCodes
{
    public const string Requested = "REQUESTED";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";

    public static bool IsValid(string? code) => code is Requested or Approved or Rejected;
}
