namespace EFootwear.Domain.Sales;

/// <summary>The statuses of sales.invoices (column status). A voided invoice stays in the books with its reason; nothing is deleted.</summary>
public static class SaleInvoiceStatusCodes
{
    public const string Posted = "POSTED";
    public const string Void = "VOID";

    public static bool IsValid(string? code) => code is Posted or Void;
}
