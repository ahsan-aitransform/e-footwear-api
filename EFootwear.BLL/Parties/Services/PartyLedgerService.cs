using EFootwear.BLL.Common;
using EFootwear.BLL.Parties.Abstractions;
using EFootwear.BLL.Parties.Contracts;
using EFootwear.Domain.Common;

namespace EFootwear.BLL.Parties.Services;

/// <summary>
/// Party Account Detail (docx 3.4): the journal lines of one party, or of all parties, between two dates, with a running balance and
/// the totals of the period. Read only: there is no way to edit or delete a ledger entry here, because posted entries are never
/// changed (a correction is a reversal posted by the document that made the entry, Part 15).
/// </summary>
public sealed class PartyLedgerService(IPartyLedgerRepository partyLedgerRepository, IPartyRepository partyRepository)
{
    public async Task<PartyLedgerResponse> GetLedgerAsync(PartyLedgerQuery query, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (query.From is { } from && query.To is { } to && from > to)
            throw new DomainException(ErrorKind.Validation, "The From date cannot be after the To date.");
        if (query.PartyId is { } partyId && await partyRepository.FindDetailAsync(partyId, cancellationToken) is null)
            throw new DomainException(ErrorKind.NotFound, "The party was not found.");

        return await partyLedgerRepository.GetLedgerAsync(query, pageRequest.Normalized(), cancellationToken);
    }
}
