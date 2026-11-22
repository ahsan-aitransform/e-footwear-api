namespace EFootwear.Domain.Parties;

/// <summary>
/// What a party owes or is owed, summed from the journal by a database trigger. Table: party.party_balances. READ ONLY for the
/// application: nothing ever writes it (Part 8: no independently editable balances).
/// Balance = DebitTotal - CreditTotal. A positive balance means the party owes the company (a customer), a negative balance
/// means the company owes the party (a vendor).
/// </summary>
public sealed class PartyBalance
{
    public Guid CompanyId { get; set; }
    public Guid PartyId { get; set; }
    public long DebitTotal { get; set; }
    public long CreditTotal { get; set; }
    public long Balance { get; set; }
    public DateOnly? LastEntryDate { get; set; }
}
