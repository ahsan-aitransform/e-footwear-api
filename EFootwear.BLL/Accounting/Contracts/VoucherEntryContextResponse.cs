namespace EFootwear.BLL.Accounting.Contracts;

/// <summary>
/// What an entry screen shows before the first keystroke: the number the next voucher will get (read-only "Voucher#"). It is a preview;
/// the number is allocated when the voucher is posted. The date field defaults to the user's own local date in the browser.
/// </summary>
public sealed record VoucherEntryContextResponse(string VoucherTypeCode, Guid StoreId, long NextVoucherNumber);
