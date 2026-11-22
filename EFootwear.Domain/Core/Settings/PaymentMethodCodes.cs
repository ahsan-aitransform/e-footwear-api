namespace EFootwear.Domain.Core.Settings;

/// <summary>
/// The payment methods the sales tables accept. They mirror the CHECK constraint of sales.payments.method: adding a method
/// needs a schema change in the Sales module first, then one constant here.
/// </summary>
public static class PaymentMethodCodes
{
    public const string Cash = "CASH";
    public const string Card = "CARD";
    public const string BankTransfer = "BANK_TRANSFER";
    public const string Cheque = "CHEQUE";
    public const string Online = "ONLINE";

    /// <summary>All methods, in the order a screen lists them when nothing is configured.</summary>
    public static readonly IReadOnlyList<string> All = [Cash, Card, BankTransfer, Cheque, Online];
}
