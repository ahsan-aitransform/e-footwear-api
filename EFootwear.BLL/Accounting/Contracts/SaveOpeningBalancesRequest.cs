namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>Saves the opening balances of one store (docx 2.18 "Save"). The lines that did not change are skipped.</summary>
public sealed record SaveOpeningBalancesRequest(Guid StoreId, DateOnly EntryDate, IReadOnlyList<OpeningBalanceLineRequest> Lines);
