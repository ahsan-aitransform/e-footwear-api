namespace EFootwear.BLL.Sales.Contracts;

/// <summary>The numbers and totals of one store and day that the repository reads for <see cref="SaleEntryContextResponse"/>.</summary>
public sealed record SaleStoreCountersResponse(
    string StoreName,
    long NextInvoiceNumber,
    long NextDailyBillNumber,
    long NextReturnInvoiceNumber,
    long TodaySaleAmount,
    string RoundingModeCode);
