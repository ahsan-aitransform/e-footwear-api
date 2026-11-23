namespace EFootwear.BLL.Sales.Contracts;

/// <summary>
/// One returned item. It names the sale line it came from (Part 12 "original sale-line linkage"); the price and the cost are taken from that line.
/// <paramref name="ItemConditionCode"/> is SELLABLE or DAMAGED (default SELLABLE).
/// </summary>
public sealed record ReturnedLineRequest(Guid OriginalLineId, decimal Quantity, string? ReasonText, string? ItemConditionCode);
