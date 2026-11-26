namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>
/// Approves the pending commission of one store for a range of sale dates (hr.approve_commissions). Salesman commission is approved as it stands;
/// manager commission waits until the store's sales in the range reach the minimum store sales of the plan.
/// </summary>
public sealed record ApproveCommissionsRequest(Guid StoreId, DateOnly FromDate, DateOnly ToDate);
