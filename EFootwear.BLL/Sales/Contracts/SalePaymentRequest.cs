namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// Money kept in one account. <paramref name="Amount"/> is what stays with the shop (after the change is handed back), in whole rupees.
/// The method is one of the codes of the payment methods of the company (CASH, CARD, BANK_TRANSFER, CHEQUE, ONLINE); the account is a cash or bank account.
/// </summary>
public sealed record SalePaymentRequest(string PaymentMethodCode, Guid PaymentAccountId, long Amount, string? ReferenceNumber);
