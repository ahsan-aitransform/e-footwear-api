namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// Exchange Invoice (docx 5.3): returned items and new items in one operation, posted as a return invoice and a sale invoice that point at each other.
/// The payments belong to the new-sale part and cover the difference. Without a customer ("Cash Sale") the difference must be paid in full.
/// </summary>
public sealed record CreateExchangeRequest(
    Guid StoreId,
    DateOnly ExchangeDate,
    Guid ClientTransactionId,
    Guid? CustomerId,
    Guid? SalesmanStaffId,
    string? WalkInCustomerName,
    string? WalkInCustomerMobile,
    Guid? PosTerminalDeviceId,
    string? Description,
    IReadOnlyList<ReturnedLineRequest> ReturnedLines,
    IReadOnlyList<SaleLineRequest> NewSaleLines,
    IReadOnlyList<SalePaymentRequest> Payments,
    long CashReturnedAmount);
