using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Abstractions;
using EFootwear.BLL.Staff.Abstractions;
using EFootwear.BLL.Staff.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;

namespace EFootwear.BLL.Staff.Services;

/// <summary>
/// Staff Account Detail: the journal lines of one staff member, or of all staff, between two dates, with a running balance and the totals of the
/// period. Read only: posted entries are never changed (a correction is a reversal posted by the document that made the entry).
/// </summary>
public sealed class StaffLedgerService(IContactLedgerRepository contactLedgerRepository, IStaffMemberRepository staffRepository)
{
    public async Task<StaffLedgerResponse> GetLedgerAsync(StaffLedgerQuery query, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (query.From is { } from && query.To is { } to && from > to)
            throw new DomainException(ErrorKind.Validation, "The From date cannot be after the To date.");
        if (query.StaffId is { } staffId && await staffRepository.FindDetailAsync(staffId, cancellationToken) is null)
            throw new DomainException(ErrorKind.NotFound, "The staff member was not found.");

        var normalizedPageRequest = pageRequest.Normalized();
        var data = await contactLedgerRepository.GetLedgerAsync(ContactKindCodes.Staff, query.StaffId, query.From, query.To, normalizedPageRequest, cancellationToken);
        var lines = data.Lines.Select(line => new StaffLedgerLineResponse(line.LineId, line.ContactId, line.ContactCode, line.ContactName, line.EntryDate,
                                                                        line.SourceType, line.DocumentNumber, line.Description, line.DebitAmount,
                                                                        line.CreditAmount, line.RunningBalanceAmount)).ToList();
        return new StaffLedgerResponse(query.StaffId, data.OpeningBalanceAmount,
                                       new PagedResult<StaffLedgerLineResponse>(lines, normalizedPageRequest.PageNumber, normalizedPageRequest.PageSize, data.LineCount),
                                       data.TotalDebitAmount, data.TotalCreditAmount);
    }
}
