namespace EFootwear.Domain.Inventory;

/// <summary>The reasons of inv.claims (column reason). The docx screen has a free "Description"; it is kept as the note of the claim.</summary>
public static class ProductClaimReasonCodes
{
    public const string Damaged = "DAMAGED";
    public const string Defective = "DEFECTIVE";
    public const string Lost = "LOST";
    public const string Other = "OTHER";

    public static bool IsValid(string? code) => code is Damaged or Defective or Lost or Other;
}
