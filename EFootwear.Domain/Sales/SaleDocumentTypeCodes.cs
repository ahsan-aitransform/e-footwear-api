namespace EFootwear.Domain.Sales;

/// <summary>The document types of sales.invoices (column doc_type). An exchange is a RETURN and a SALE linked to each other.</summary>
public static class SaleDocumentTypeCodes
{
    public const string Sale = "SALE";
    public const string Return = "RETURN";

    public static bool IsValid(string? code) => code is Sale or Return;
}
