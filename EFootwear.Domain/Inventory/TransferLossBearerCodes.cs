namespace EFootwear.Domain.Inventory;

/// <summary>Who carries the loss of missing pieces (inv.transfer_loss_cases.bearer). No default exists: the business has not decided it.</summary>
public static class TransferLossBearerCodes
{
    public const string Company = "COMPANY";
    public const string SenderStore = "SENDER_STORE";
    public const string ReceiverStore = "RECEIVER_STORE";

    public static bool IsValid(string? code) => code is Company or SenderStore or ReceiverStore;
}
