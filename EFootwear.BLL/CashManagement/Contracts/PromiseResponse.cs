namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>
/// A customer promise (docx 3.5 grid: Date, Customer, Promise Date, Description, Amount, Paid Amount, Remaining Amount).
/// PromiseDate is the day the promise was taken, DueDate the day the customer promised to pay. Whole rupees.
/// </summary>
public sealed record PromiseResponse(
    Guid Id,
    long? PromiseNumber,
    Guid StoreId,
    Guid CustomerId,
    string CustomerName,
    DateOnly PromiseDate,
    DateOnly DueDate,
    long PromisedAmount,
    long PaidAmount,
    long RemainingAmount,
    string StatusCode,
    string? Note,
    long RowVersion);
