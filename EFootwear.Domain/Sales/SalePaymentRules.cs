namespace EFootwear.Domain.Sales;

/// <summary>Limits on the payment rows of one sale. They mirror the checks of sales.post_sale_invoice, which stays the authority.</summary>
public static class SalePaymentRules
{
    public const int MaximumPaymentsPerInvoice = 6;
    public const int ReferenceNumberMaximumLength = 100;
}
