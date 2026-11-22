using EFootwear.Domain.Common;

namespace EFootwear.Domain.Accounting;

/// <summary>
/// An account of the chart of accounts. Table: acct.accounts. Accounts that carry a <see cref="SystemRole"/> (cash, bank, party
/// control, sales, stock ...) are looked up by role by the posting functions of the database, so they can be renamed but never
/// deleted. Balances are never stored here: they come from the journal.
/// </summary>
public sealed class GeneralLedgerAccount : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string MainTypeCode { get; set; } = "";
    public string GroupCode { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? ParentAccountId { get; set; }

    /// <summary>The store that owns the account; the nil id means the head office.</summary>
    public Guid StoreId { get; set; }

    public string? SystemRole { get; set; }
    public bool IsPostable { get; set; } = true;
    public bool IsPartyControl { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
