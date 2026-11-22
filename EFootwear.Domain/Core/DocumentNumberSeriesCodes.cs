namespace EFootwear.Domain.Core;

/// <summary>
/// The document number series of core.number_series (column series_code). Every module that numbers a document calls
/// <c>core.next_number(company, store, series_code[, period])</c> with one of these constants, so the Document Numbering screen
/// and the module always talk about the same series. The names follow the document types in the schema (sales.invoices.doc_type,
/// acct.vouchers.voucher_type, inv.documents.doc_type, orders.orders.order_type, inv.transfers).
/// </summary>
public static class DocumentNumberSeriesCodes
{
    public const string SaleInvoice = "SALE_INVOICE";
    public const string SaleReturnInvoice = "SALE_RETURN_INVOICE";
    public const string PurchaseInvoice = "PURCHASE_INVOICE";
    public const string PurchaseReturnInvoice = "PURCHASE_RETURN_INVOICE";
    public const string PaymentVoucher = "PAYMENT_VOUCHER";
    public const string ReceiptVoucher = "RECEIPT_VOUCHER";
    public const string ExpenseVoucher = "EXPENSE_VOUCHER";
    public const string JournalVoucher = "JOURNAL_VOUCHER";
    public const string StockAdjustment = "STOCK_ADJUSTMENT";
    public const string StockAudit = "STOCK_AUDIT";
    public const string ProductClaim = "PRODUCT_CLAIM";
    public const string StockTransfer = "STOCK_TRANSFER";
    public const string SaleOrder = "SALE_ORDER";
    public const string SaleReturnOrder = "SALE_RETURN_ORDER";
    public const string PurchaseOrder = "PURCHASE_ORDER";
    public const string PurchaseReturnOrder = "PURCHASE_RETURN_ORDER";
    public const string VendorPurchaseOrder = "VENDOR_PURCHASE_ORDER";
    public const string CompanyPurchaseOrder = "COMPANY_PURCHASE_ORDER";

    /// <summary>Company-wide product codes (catalog.products.code), always on the head-office series; allocated by catalog.allocate_next_product_code().</summary>
    public const string ProductCode = "PRODUCT_CODE";

    public static readonly IReadOnlyList<string> All =
    [
        SaleInvoice, SaleReturnInvoice, PurchaseInvoice, PurchaseReturnInvoice,
        PaymentVoucher, ReceiptVoucher, ExpenseVoucher, JournalVoucher,
        StockAdjustment, StockAudit, ProductClaim, StockTransfer,
        SaleOrder, SaleReturnOrder, PurchaseOrder, PurchaseReturnOrder, VendorPurchaseOrder, CompanyPurchaseOrder,
        ProductCode,
    ];
}
