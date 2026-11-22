namespace EFootwear.BLL.Parties.Contracts;

/// <summary>
/// The filters of Party Account Detail (docx 3.4: Party, From, To). A null party means all parties (the docx default). A null
/// date is open-ended. Bound from the query string next to the page request.
/// </summary>
public sealed record PartyLedgerQuery(Guid? PartyId = null, DateOnly? From = null, DateOnly? To = null);
