namespace EFootwear.Domain.Inventory;

/// <summary>The phases of inv.transfer_scans (column phase): the sending store scans while the transfer is a draft, the receiving store scans after dispatch.</summary>
public static class TransferScanPhaseCodes
{
    public const string Send = "SEND";
    public const string Receive = "RECEIVE";
}
