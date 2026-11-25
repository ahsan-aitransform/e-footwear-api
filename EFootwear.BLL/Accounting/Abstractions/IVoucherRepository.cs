using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Accounting.Abstractions;

/// <summary>
/// Vouchers (docx 6.1 to 6.6). Posting and voiding go through the database functions acct.post_voucher and acct.void_voucher, which write the
/// journal entry and the voucher together; the application never inserts journal rows itself (Part 15). Implemented in EFootwear.DAL.
/// </summary>
public interface IVoucherRepository
{
    /// <summary>The number the next voucher of this type will get in this store. It is a preview: the number is allocated when the voucher is posted.</summary>
    Task<long> GetNextVoucherNumberAsync(Guid storeId, string voucherTypeCode, CancellationToken cancellationToken);

    /// <summary>Posts the voucher and returns its id. A repeated client transaction id returns the id of the voucher posted the first time.</summary>
    Task<Guid> PostAsync(string voucherTypeCode, PostVoucherRequest request, CancellationToken cancellationToken);

    Task<VoucherResponse?> FindByIdAsync(Guid voucherId, CancellationToken cancellationToken);

    Task<Guid?> FindIdByNumberAsync(string voucherTypeCode, long voucherNumber, Guid storeId, CancellationToken cancellationToken);

    Task<PagedResult<VoucherListItemResponse>> SearchAsync(VoucherSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken);

    Task<VoucherReportResponse> BuildReportAsync(VoucherSearchFilter filter, int rowLimit, CancellationToken cancellationToken);

    /// <summary>Changes the narration kept on the voucher. Returns false when no voucher with this id and row version exists (missing or changed by someone else).</summary>
    Task<bool> UpdateDescriptionAsync(Guid voucherId, string? description, long expectedRowVersion, CancellationToken cancellationToken);

    /// <summary>Voids the voucher by a reversing journal entry (database function acct.void_voucher).</summary>
    Task VoidAsync(Guid voucherId, string reason, CancellationToken cancellationToken);

    /// <summary>Accounts, customers, vendors or staff that can be chosen as "To" / "From", with their current balance. <c>kindCode</c> is ACCOUNT, CUSTOMER, VENDOR or STAFF.</summary>
    Task<IReadOnlyList<VoucherTargetOptionResponse>> SearchTargetsAsync(
        string kindCode, string? searchText, string? groupCode, int limit, CancellationToken cancellationToken);
}
