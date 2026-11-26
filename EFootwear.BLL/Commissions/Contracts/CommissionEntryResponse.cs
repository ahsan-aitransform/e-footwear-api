namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>
/// One commission entry: the commission one staff member earned on one sold line (EARN), or the entry that takes it back (REVERSAL, negative amounts).
/// The rate or fixed amount is the one used on the day, so a later change of the plan never rewrites it.
/// </summary>
public sealed record CommissionEntryResponse(
    Guid Id,
    Guid StoreId,
    string StoreName,
    Guid StaffId,
    string StaffCode,
    string StaffName,
    Guid PlanId,
    string PlanName,
    DateOnly SaleDate,
    Guid InvoiceId,
    long InvoiceNumber,
    string EntryTypeCode,
    string CalculationTypeCode,
    decimal? Rate,
    long? FixedAmountPerPiece,
    decimal Quantity,
    long BaseAmount,
    long CommissionAmount,
    string StatusCode,
    DateTimeOffset? ApprovedAt,
    DateOnly? SalaryMonth);
