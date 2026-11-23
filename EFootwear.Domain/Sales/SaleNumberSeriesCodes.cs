namespace EFootwear.Domain.Sales;

/// <summary>
/// The number series the sales screens read (core.number_series). The invoice numbers come from the shared <c>DocumentNumberSeriesCodes</c>
/// (SALE_INVOICE and SALE_RETURN_INVOICE, one counter per store). The daily bill number restarts every day: its rows use the sale date as period key,
/// so it is kept here and not in the Document Numbering screen, which only moves series that never reset.
/// </summary>
public static class SaleNumberSeriesCodes
{
    public const string DailyBill = "SALE_DAILY_BILL";
}
