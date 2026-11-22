namespace EFootwear.BLL.CashManagement.Contracts;

/// <summary>Edit an open promise (docx 3.5 Edit). The customer and the store of a promise never change. RowVersion is the value the screen loaded.</summary>
public sealed record UpdatePromiseRequest(DateOnly DueDate, long PromisedAmount, string? Note, long RowVersion);
