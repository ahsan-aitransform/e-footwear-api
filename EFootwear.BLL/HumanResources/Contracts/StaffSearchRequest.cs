namespace EFootwear.BLL.HumanResources.Contracts;

/// <summary>Filters of the staff list (docx 3.1 staff parties). The text search comes with the page request. Former staff are hidden unless asked for.</summary>
public sealed record StaffSearchRequest(Guid? StoreId, bool IncludeFormerStaff = false);
