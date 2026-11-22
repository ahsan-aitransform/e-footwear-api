namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>New promise (docx 3.5). The promise number is allocated by the server per store. The amount is in whole rupees and must be above zero.</summary>
public sealed record CreatePromiseRequest(Guid StoreId, Guid CustomerPartyId, DateOnly PromiseDate, DateOnly DueDate, long PromisedAmount, string? Note);
