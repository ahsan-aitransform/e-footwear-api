namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// New Sale (docx 5.1). <paramref name="ClientTransactionId"/> is made by the screen when the cashier starts the sale and sent again unchanged on every retry:
/// the server then returns the invoice it already made instead of posting a second one (Part 23). The company is never sent: it comes from the session.
/// Each payment amount is what STAYS in the shop (the cash received minus the change). <paramref name="CashReturnedAmount"/> is the cash handed back to the customer
/// (the docx "Cash Return"); the invoice then shows paid = kept + returned, which is the cash the customer handed over.
/// </summary>
public sealed record CreateSaleInvoiceRequest(
    Guid StoreId,
    DateOnly SaleDate,
    Guid ClientTransactionId,
    Guid? CustomerPartyId,
    Guid? SalesmanPartyId,
    string? WalkInCustomerName,
    string? WalkInCustomerMobile,
    Guid? PosTerminalDeviceId,
    string? Description,
    IReadOnlyList<SaleLineRequest> Lines,
    IReadOnlyList<SalePaymentRequest> Payments,
    long CashReturnedAmount);
