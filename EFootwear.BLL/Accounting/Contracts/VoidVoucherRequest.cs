namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>The docx "Delete" button, done the accounting way: the voucher is voided by a reversing entry and keeps its record. A reason is required.</summary>
public sealed record VoidVoucherRequest(string Reason);
