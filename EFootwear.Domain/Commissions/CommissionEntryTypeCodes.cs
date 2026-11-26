namespace EFootwear.Domain.Commissions;

/// <summary>An EARN entry is the commission of a sold line; a REVERSAL entry takes it back (a return or a voided invoice) and points at the EARN entry it reverses.</summary>
public static class CommissionEntryTypeCodes
{
    public const string Earn = "EARN";
    public const string Reversal = "REVERSAL";
}
