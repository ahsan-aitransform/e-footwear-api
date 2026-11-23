namespace EFootwear.BLL.HumanResources.Contracts;

/// <summary>The staff page: the profile, the salary history and the store history. <paramref name="RowVersion"/> is sent back when the salary terms are saved.</summary>
public sealed record StaffDetailResponse(
    StaffListItemResponse Profile,
    long RowVersion,
    IReadOnlyList<SalaryTermResponse> SalaryTerms,
    IReadOnlyList<StaffTransferHistoryItemResponse> TransferHistory);
