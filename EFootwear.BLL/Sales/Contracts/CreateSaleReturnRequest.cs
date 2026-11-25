namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// Sale Return (docx 5.2). The returned lines name their original sale lines; the account of the customer is credited (or the suspense account when there
/// is no customer). Paying cash back is not modelled: that refund rule is [BUSINESS DECISION REQUIRED].
/// </summary>
public sealed record CreateSaleReturnRequest(
    Guid StoreId,
    DateOnly ReturnDate,
    Guid ClientTransactionId,
    Guid? CustomerId,
    string? Description,
    IReadOnlyList<ReturnedLineRequest> Lines);
