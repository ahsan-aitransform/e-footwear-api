using EFootwear.BLL.Common;

namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>One page of promises plus the totals of everything the filters match (the "Total" box of docx 3.5). Cancelled promises are left out of the totals.</summary>
public sealed record PromiseListResponse(
    PagedResult<PromiseResponse> Promises,
    long TotalPromisedAmount,
    long TotalPaidAmount,
    long TotalRemainingAmount);
