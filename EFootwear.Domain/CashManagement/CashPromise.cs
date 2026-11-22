using EFootwear.Domain.Common;

namespace EFootwear.Domain.CashManagement;

/// <summary>
/// A customer's promise to pay a certain amount by a certain date (docx 3.5). Table: cash.promises. Store level: the promise
/// belongs to the store that took it. A promise is never deleted: "Delete" cancels it (status CANCELLED), so the history stays.
/// </summary>
public sealed class CashPromise : IHasRowVersion
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }

    /// <summary>Allocated per store by core.next_number when the promise is saved (the old "Promise Id").</summary>
    public long? PromiseNumber { get; set; }

    public Guid StoreId { get; set; }
    public Guid CustomerPartyId { get; set; }
    public DateOnly PromiseDate { get; set; }
    public DateOnly DueDate { get; set; }

    /// <summary>Whole rupees.</summary>
    public long PromisedAmount { get; set; }

    public long PaidAmount { get; set; }
    public string Status { get; set; } = PromiseStatusCodes.Open;
    public string? Note { get; set; }
    public Guid? CreatedBy { get; set; }
    public long RowVersion { get; set; }
}
