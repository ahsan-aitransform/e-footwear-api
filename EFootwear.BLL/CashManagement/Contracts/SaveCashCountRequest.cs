namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>
/// Saves the cash count of one store and day (docx 3.6 Save). There is one count per store and day: saving again for the same day
/// replaces that day's figures (the schema keeps one row per store and day). The total counted is computed by the server from the
/// denominations. Payments, credit sales and credit received are figures typed by the person counting; Net Sale is not stored.
/// RowVersion is needed only when a count already exists for that day and is being changed.
/// </summary>
public sealed record SaveCashCountRequest(
    Guid StoreId,
    DateOnly CountDate,
    long OpeningCash,
    IReadOnlyList<CashCountDenominationLine> Denominations,
    long? PaymentsAmount,
    long? CreditSalesAmount,
    long? CreditReceivedAmount,
    string? Note,
    long? RowVersion);
