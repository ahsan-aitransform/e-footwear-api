namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>
/// The filters of View Cash Detail (docx 3.7: From / To; the period shortcuts Current Week, Current Month and Previous Month are
/// calculated by the screen and sent as dates). A null store means every store the caller may access.
/// </summary>
public sealed record CashCountSearchRequest(Guid? StoreId = null, DateOnly? From = null, DateOnly? To = null);
