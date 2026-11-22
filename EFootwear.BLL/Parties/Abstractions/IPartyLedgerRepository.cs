using EFootwear.BLL.Common;
using EFootwear.BLL.Parties.Contracts;

namespace EFootwear.BLL.Parties.Abstractions;

/// <summary>
/// Read-only access to the party ledger (journal lines that carry a party). Nothing here writes: the journal is append-only and
/// balances are derived. Implemented in EFootwear.DAL/Parties/Repositories/PartyLedgerRepository.cs.
/// </summary>
public interface IPartyLedgerRepository
{
    Task<PartyLedgerResponse> GetLedgerAsync(PartyLedgerQuery query, PageRequest pageRequest, CancellationToken cancellationToken);
}
