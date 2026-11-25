using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Abstractions;
using EFootwear.BLL.Vendors.Abstractions;
using EFootwear.BLL.Vendors.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;

namespace EFootwear.BLL.Vendors.Services;

/// <summary>
/// Vendor Account Detail: the journal lines of one vendor, or of all vendors, between two dates, with a running balance and the totals of the
/// period. Read only: there is no way to edit or delete a ledger entry here, because posted entries are never changed (a correction is a
/// reversal posted by the document that made the entry).
/// </summary>
public sealed class VendorLedgerService(IContactLedgerRepository contactLedgerRepository, IVendorRepository vendorRepository)
{
    public async Task<VendorLedgerResponse> GetLedgerAsync(VendorLedgerQuery query, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (query.From is { } from && query.To is { } to && from > to)
            throw new DomainException(ErrorKind.Validation, "The From date cannot be after the To date.");
        if (query.VendorId is { } vendorId && await vendorRepository.FindDetailAsync(vendorId, cancellationToken) is null)
            throw new DomainException(ErrorKind.NotFound, "The vendor was not found.");

        var normalizedPageRequest = pageRequest.Normalized();
        var data = await contactLedgerRepository.GetLedgerAsync(ContactKindCodes.Vendor, query.VendorId, query.From, query.To, normalizedPageRequest, cancellationToken);
        var lines = data.Lines.Select(line => new VendorLedgerLineResponse(line.LineId, line.ContactId, line.ContactCode, line.ContactName, line.EntryDate,
                                                                          line.SourceType, line.DocumentNumber, line.Description, line.DebitAmount,
                                                                          line.CreditAmount, line.RunningBalanceAmount)).ToList();
        return new VendorLedgerResponse(query.VendorId, data.OpeningBalanceAmount,
                                         new PagedResult<VendorLedgerLineResponse>(lines, normalizedPageRequest.PageNumber, normalizedPageRequest.PageSize, data.LineCount),
                                         data.TotalDebitAmount, data.TotalCreditAmount);
    }
}
