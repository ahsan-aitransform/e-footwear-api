using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Accounting.Contracts;
using EFootwear.BLL.Common;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Accounting.Services;

/// <summary>
/// Payment, Receipt, Expense and Journal vouchers, View Voucher and the Voucher Report (docx 6.1 to 6.6). A voucher is one balanced journal
/// entry of two lines, posted by the database function acct.post_voucher; the posted entry is never edited. "Update Description" only
/// changes the narration kept on the voucher, and "Delete" voids the voucher with a reversing entry. The service checks what the caller can fix
/// (store access, amount, who is on which side) and leaves the accounting rules (balance, closed period, account state) to the database.
/// Which account types may be used on which voucher type is a business policy ([BUSINESS DECISION REQUIRED]) and is not enforced.
/// </summary>
public sealed class VoucherService(
    IVoucherRepository voucherRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<VoucherService> logger)
{
    public async Task<VoucherEntryContextResponse> GetEntryContextAsync(string voucherTypeCode, Guid storeId, CancellationToken cancellationToken)
    {
        EnsureVoucherTypeIsValid(voucherTypeCode);
        EnsureStoreAccess(storeId);
        var nextNumber = await voucherRepository.GetNextVoucherNumberAsync(storeId, voucherTypeCode, cancellationToken);
        return new VoucherEntryContextResponse(voucherTypeCode, storeId, nextNumber);
    }

    public async Task<VoucherResponse> PostAsync(string voucherTypeCode, PostVoucherRequest request, CancellationToken cancellationToken)
    {
        EnsureVoucherTypeIsValid(voucherTypeCode);
        EnsureStoreAccess(request.StoreId);
        VoucherRules.EnsureAmountIsValid(request.Amount);
        EnsureTargetIsWellFormed(request.DebitTarget, "To (debit)");
        EnsureTargetIsWellFormed(request.CreditTarget, "From (credit)");
        if (request.DebitTarget.PartyId.HasValue && request.CreditTarget.PartyId.HasValue)
            throw new DomainException(ErrorKind.Validation,
                "Two parties cannot be posted against each other: use an account (for example a cash or bank account) in between.");
        if (request.PaymentMode is not null && !VoucherPaymentModeCodes.IsValid(request.PaymentMode))
            throw new DomainException(ErrorKind.Validation, "The payment type is not valid.");
        EnsureAllocationsAreWellFormed(voucherTypeCode, request);

        var cleanedRequest = request with
        {
            ReferenceNumber = VoucherRules.CleanText(request.ReferenceNumber, VoucherRules.ReferenceNumberMaximumLength, "The reference number"),
            Description = VoucherRules.CleanText(request.Description, VoucherRules.DescriptionMaximumLength, "The description"),
        };

        var voucherId = await voucherRepository.PostAsync(voucherTypeCode, cleanedRequest, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var voucher = await voucherRepository.FindByIdAsync(voucherId, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The voucher was not found after saving.");
        logger.LogInformation("{VoucherType} voucher {VoucherNumber} ({VoucherId}) of {Amount} rupees posted in store {StoreId} of company {CompanyId} by user {UserId}",
            voucherTypeCode, voucher.VoucherNumber, voucher.Id, voucher.Amount, voucher.StoreId, currentUser.CompanyId, currentUser.UserId);
        return voucher;
    }

    public async Task<VoucherResponse> GetByIdAsync(Guid voucherId, CancellationToken cancellationToken) =>
        await voucherRepository.FindByIdAsync(voucherId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The voucher was not found.");

    public async Task<VoucherResponse> GetByNumberAsync(string voucherTypeCode, long voucherNumber, Guid storeId, CancellationToken cancellationToken)
    {
        EnsureVoucherTypeIsValid(voucherTypeCode);
        EnsureStoreAccess(storeId);
        var voucherId = await voucherRepository.FindIdByNumberAsync(voucherTypeCode, voucherNumber, storeId, cancellationToken)
                        ?? throw new DomainException(ErrorKind.NotFound, "No voucher of this type has that number in this store.");
        return await GetByIdAsync(voucherId, cancellationToken);
    }

    public Task<PagedResult<VoucherListItemResponse>> SearchAsync(VoucherSearchFilter filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        EnsureFilterIsValid(filter);
        return voucherRepository.SearchAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    public Task<VoucherReportResponse> BuildReportAsync(VoucherSearchFilter filter, CancellationToken cancellationToken)
    {
        EnsureFilterIsValid(filter);
        if (filter.FromDate is null || filter.ToDate is null)
            throw new DomainException(ErrorKind.Validation, "Choose the From and To dates of the report.");
        return voucherRepository.BuildReportAsync(filter, VoucherRules.ReportRowLimit, cancellationToken);
    }

    public async Task<VoucherResponse> UpdateDescriptionAsync(Guid voucherId, UpdateVoucherDescriptionRequest request, CancellationToken cancellationToken)
    {
        var description = VoucherRules.CleanText(request.Description, VoucherRules.DescriptionMaximumLength, "The description");
        var updated = await voucherRepository.UpdateDescriptionAsync(voucherId, description, request.ExpectedRowVersion, cancellationToken);
        if (!updated)
        {
            var exists = await voucherRepository.FindByIdAsync(voucherId, cancellationToken) is not null;
            throw exists
                ? new DomainException(ErrorKind.Conflict, "Someone else changed this voucher. Open it again and repeat your change.", ErrorCodes.ConcurrentUpdate)
                : new DomainException(ErrorKind.NotFound, "The voucher was not found.");
        }
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Description of voucher {VoucherId} changed in company {CompanyId} by user {UserId}", voucherId, currentUser.CompanyId, currentUser.UserId);
        return await GetByIdAsync(voucherId, cancellationToken);
    }

    public async Task<VoucherResponse> VoidAsync(Guid voucherId, VoidVoucherRequest request, CancellationToken cancellationToken)
    {
        var reason = VoucherRules.CleanText(request.Reason, VoucherRules.VoidReasonMaximumLength, "The reason");
        if (reason is null)
            throw new DomainException(ErrorKind.Validation, "Give the reason for voiding this voucher.");

        var voucher = await GetByIdAsync(voucherId, cancellationToken);
        if (voucher.StatusCode == VoucherStatusCodes.Void)
            throw new DomainException(ErrorKind.Conflict, "This voucher is already void.");

        await voucherRepository.VoidAsync(voucherId, reason, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("{VoucherType} voucher {VoucherNumber} ({VoucherId}) voided in company {CompanyId} by user {UserId}",
            voucher.VoucherTypeCode, voucher.VoucherNumber, voucherId, currentUser.CompanyId, currentUser.UserId);
        return await GetByIdAsync(voucherId, cancellationToken);
    }

    public Task<IReadOnlyList<VoucherTargetOptionResponse>> SearchTargetsAsync(
        string kindCode, string? searchText, string? partyTypeCode, string? groupCode, CancellationToken cancellationToken)
    {
        if (kindCode is not (VoucherTargetKindCodes.Account or VoucherTargetKindCodes.Party))
            throw new DomainException(ErrorKind.Validation, "Choose accounts or parties.");
        if (partyTypeCode is not null && !AccountReportRules.IsValidPartyType(partyTypeCode))
            throw new DomainException(ErrorKind.Validation, "That party type does not exist.");
        var cleanedSearchText = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
        return voucherRepository.SearchTargetsAsync(kindCode, cleanedSearchText, partyTypeCode, groupCode, AccountReportRules.LookupResultLimit, cancellationToken);
    }

    private static void EnsureVoucherTypeIsValid(string voucherTypeCode)
    {
        if (!VoucherTypeCodes.IsValid(voucherTypeCode))
            throw new DomainException(ErrorKind.Validation, "That voucher type does not exist.");
    }

    private void EnsureStoreAccess(Guid storeId)
    {
        // The head office (nil id) is reachable only by people who may see every store.
        if (!currentUser.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
    }

    private void EnsureFilterIsValid(VoucherSearchFilter filter)
    {
        if (filter.VoucherTypeCode is not null) EnsureVoucherTypeIsValid(filter.VoucherTypeCode);
        if (filter.StatusCode is not null && !VoucherStatusCodes.IsValid(filter.StatusCode))
            throw new DomainException(ErrorKind.Validation, "That voucher status does not exist.");
        if (filter.StoreId is { } storeId) EnsureStoreAccess(storeId);
        if (filter.FromDate is { } from && filter.ToDate is { } to && from > to)
            throw new DomainException(ErrorKind.Validation, "The From date cannot be after the To date.");
    }

    private static void EnsureTargetIsWellFormed(VoucherTargetRequest target, string sideTitle)
    {
        if (target.AccountId.HasValue == target.PartyId.HasValue)
            throw new DomainException(ErrorKind.Validation, $"Choose one account or one party for {sideTitle}.");
        if (target.AccountId == Guid.Empty || target.PartyId == Guid.Empty)
            throw new DomainException(ErrorKind.Validation, $"The choice for {sideTitle} is not valid.");
    }

    private static void EnsureAllocationsAreWellFormed(string voucherTypeCode, PostVoucherRequest request)
    {
        if (request.Allocations is not { Count: > 0 } allocations) return;
        if (!VoucherTypeCodes.CanSettleInvoices(voucherTypeCode))
            throw new DomainException(ErrorKind.Validation, "Only a payment to a vendor or a receipt from a customer can settle invoices.");

        var settledParty = voucherTypeCode == VoucherTypeCodes.Payment ? request.DebitTarget.PartyId : request.CreditTarget.PartyId;
        if (settledParty is null)
            throw new DomainException(ErrorKind.Validation,
                voucherTypeCode == VoucherTypeCodes.Payment
                    ? "Choose the vendor in the To box to settle purchase invoices."
                    : "Choose the customer in the From box to settle sale invoices.");
        if (allocations.Count > VoucherRules.AllocationCountMaximum)
            throw new DomainException(ErrorKind.Validation, $"A voucher can settle at most {VoucherRules.AllocationCountMaximum} invoices.");
        if (allocations.Any(allocation => allocation.InvoiceId == Guid.Empty || allocation.Amount <= 0))
            throw new DomainException(ErrorKind.Validation, "Every settled invoice needs an amount above zero.");
        if (allocations.Sum(allocation => allocation.Amount) > request.Amount)
            throw new DomainException(ErrorKind.Validation, "The settled invoices add up to more than the voucher amount.");
    }
}
