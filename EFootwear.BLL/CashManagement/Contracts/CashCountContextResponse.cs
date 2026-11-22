namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>
/// What the Cash Calculator needs before it saves: the cash the books say the store holds up to the day, and the count already saved
/// for that day (so the screen can show and change it).
/// </summary>
public sealed record CashCountContextResponse(Guid StoreId, DateOnly CountDate, long ExpectedCash, CashCountResponse? ExistingCount);
