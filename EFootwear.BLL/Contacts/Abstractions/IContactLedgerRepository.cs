using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Contracts;

namespace EFootwear.BLL.Contacts.Abstractions;

/// <summary>
/// Read-only access to the ledger of customers, vendors or staff (journal lines that carry one of them). Nothing here writes: the journal is
/// append-only and balances are derived. Implemented in EFootwear.DAL/Contacts/Repositories/ContactLedgerRepository.cs.
/// </summary>
public interface IContactLedgerRepository
{
    /// <param name="kindCode">CUSTOMER, VENDOR or STAFF (see ContactKindCodes).</param>
    /// <param name="contactId">One contact, or null for every contact of that kind.</param>
    Task<ContactLedgerData> GetLedgerAsync(string kindCode, Guid? contactId, DateOnly? fromDate, DateOnly? toDate, PageRequest pageRequest,
                                           CancellationToken cancellationToken);
}
