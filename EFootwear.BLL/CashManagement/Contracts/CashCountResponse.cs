namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>
/// A saved cash count. ExpectedCash is the "Computer Cash" (what the books say the store holds), CountedCash the "Original Cash"
/// (the notes counted), DifferenceAmount = CountedCash - ExpectedCash (docx 3.7). All whole rupees.
/// </summary>
public sealed record CashCountResponse(
    Guid Id,
    Guid StoreId,
    DateOnly CountDate,
    long OpeningCash,
    long ExpectedCash,
    long CountedCash,
    long DifferenceAmount,
    IReadOnlyList<CashCountDenominationLine> Denominations,
    long? PaymentsAmount,
    long? CreditSalesAmount,
    long? CreditReceivedAmount,
    string? Note,
    Guid? CountedByUserId,
    string? CountedByName,
    DateTimeOffset CreatedAt,
    long RowVersion);
