namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// What a sale, return or exchange screen needs when it opens for a store and a day: the numbers the next document will get (read-only previews, the real
/// number is given when saving), the sales of the day so far, how the company rounds money, the payment methods, and what to print.
/// <paramref name="MaximumManualDiscountPercentage"/> is the limit the company has set (null = no limit configured).
/// </summary>
public sealed record SaleEntryContextResponse(
    Guid StoreId,
    string StoreName,
    DateOnly BusinessDate,
    long NextInvoiceNumber,
    long NextDailyBillNumber,
    long NextReturnInvoiceNumber,
    long TodaySaleAmount,
    string RoundingModeCode,
    decimal? MaximumManualDiscountPercentage,
    IReadOnlyList<SalePaymentMethodResponse> PaymentMethods,
    SaleBusinessHeaderResponse BusinessHeader,
    SalePrintDefaultsResponse PrintDefaults);
