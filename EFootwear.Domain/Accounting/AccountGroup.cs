using EFootwear.Domain.Common;

namespace EFootwear.Domain.Accounting;

/// <summary>
/// A second-level group of the chart of accounts, for example "102 Bank" under Assets (docx 2.2 and 2.3). Table: acct.account_groups.
/// The key is the company plus the three-digit group code; the first digit is the main type.
/// </summary>
public sealed class AccountGroup : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public string GroupCode { get; set; } = "";
    public string MainTypeCode { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
