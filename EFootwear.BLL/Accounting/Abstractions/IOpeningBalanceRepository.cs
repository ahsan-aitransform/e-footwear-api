using EFootwear.BLL.Accounting.Contracts;
using EFootwear.Domain.Contacts;

namespace EFootwear.BLL.Accounting.Abstractions;

/// <summary>
/// Data access for opening balances (docx 2.18). Posting is done by the database function acct.post_opening_balance, which
/// reverses the balance in force and posts the new one, so the journal stays append-only (Part 15).
/// Implemented in EFootwear.DAL/Accounting/Repositories/OpeningBalanceRepository.cs.
/// </summary>
public interface IOpeningBalanceRepository
{
    /// <summary>
    /// The lines of the screen for a store: its accounts (not stock accounts, not the subledger control account), and for the head
    /// office also every customer, vendor and staff member, each with the opening balance in force.
    /// </summary>
    Task<IReadOnlyList<OpeningBalanceRowResponse>> ListRowsAsync(Guid storeId, CancellationToken cancellationToken);

    /// <summary>Posts or replaces one opening balance (zero amounts clear it). Exactly one of account / contact (customer, vendor or staff member) is given.</summary>
    Task PostAsync(Guid? accountId, ContactReference? contact, Guid storeId, long debitAmount, long creditAmount, DateOnly entryDate,
                   CancellationToken cancellationToken);
}
