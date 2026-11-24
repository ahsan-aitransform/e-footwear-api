namespace EFootwear.Domain.Inventory;

/// <summary>The statuses of inv.transfer_loss_cases (column status).</summary>
public static class TransferLossCaseStatusCodes
{
    public const string Open = "OPEN";
    public const string Assigned = "ASSIGNED";
    public const string Found = "FOUND";
    public const string WrittenOff = "WRITTEN_OFF";

    public static bool IsValid(string? code) => code is Open or Assigned or Found or WrittenOff;
}
