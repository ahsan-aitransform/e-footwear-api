namespace EFootwear.Domain.Inventory;

/// <summary>Which side of a transfer a list is about, seen from one store: sent by it, received by it, or both.</summary>
public static class TransferDirectionCodes
{
    public const string All = "ALL";
    public const string Outgoing = "OUTGOING";
    public const string Incoming = "INCOMING";

    public static bool IsValid(string? code) => code is All or Outgoing or Incoming;
}
