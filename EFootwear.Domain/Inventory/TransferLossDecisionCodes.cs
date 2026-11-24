namespace EFootwear.Domain.Inventory;

/// <summary>What can be decided about missing pieces (argument of inv.decide_loss_case): they were FOUND (they go into the receiver's stock) or the loss is CHARGED to a bearer.</summary>
public static class TransferLossDecisionCodes
{
    public const string Found = "FOUND";
    public const string Charge = "CHARGE";

    public static bool IsValid(string? code) => code is Found or Charge;
}
