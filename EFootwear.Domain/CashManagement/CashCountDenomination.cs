namespace EFootwear.Domain.CashManagement;

/// <summary>How many notes of one denomination were counted. Table: cash.count_denominations.</summary>
public sealed class CashCountDenomination
{
    public Guid CompanyId { get; set; }
    public Guid CountId { get; set; }
    public int Denomination { get; set; }
    public int NoteCount { get; set; }
}
