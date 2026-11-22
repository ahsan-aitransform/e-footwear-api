namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// The docx "Update Description" button. Only the narration kept on the voucher changes (audited with old and new value by the
/// database); the posted journal lines never change. <c>ExpectedRowVersion</c> must be the voucher's current row version.
/// </summary>
public sealed record UpdateVoucherDescriptionRequest(string? Description, long ExpectedRowVersion);
