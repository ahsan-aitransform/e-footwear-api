namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>
/// The filters of the Promise list (docx 3.5: From / To, default today to one week ahead, and a search by customer or description,
/// which travels in the page request). The dates filter the day the customer promised to pay. A null store means every store the
/// caller may access; a null date is open-ended.
/// </summary>
public sealed record PromiseSearchRequest(Guid? StoreId = null, DateOnly? From = null, DateOnly? To = null);
