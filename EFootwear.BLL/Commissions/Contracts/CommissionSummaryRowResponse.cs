namespace EFootwear.BLL.Commissions.Contracts;

/// <summary>
/// One staff member over a range of sale dates. The base is what the commission is worked out from (net sale or gross margin, as each plan says), net of
/// returns. Earned is the commission of the sold lines, reversed is what returns and voided invoices took back (a negative number); net is the two together.
/// Pending, approved and paid split the net by where it stands; cancelled entries are left out.
/// </summary>
public sealed record CommissionSummaryRowResponse(
    Guid StaffId,
    string StaffCode,
    string StaffName,
    long BaseAmount,
    long EarnedAmount,
    long ReversedAmount,
    long NetAmount,
    long PendingAmount,
    long ApprovedAmount,
    long PaidAmount);
