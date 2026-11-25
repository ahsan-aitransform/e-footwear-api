using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Accounting.Abstractions;
using EFootwear.BLL.Contacts.Contracts;
using EFootwear.Domain.Accounting;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;
using EFootwear.Domain.Security.Permissions;

namespace EFootwear.BLL.Contacts.Services;

/// <summary>
/// Posts the opening balance that a person typed on the New Customer / New Vendor / New Staff screen. The balance goes through the accounting
/// opening-balance mechanism (the database reverses the balance in force and posts the new one) and needs the OPENING_BALANCE permission.
/// The balance is never stored on the contact itself.
/// </summary>
public sealed class ContactOpeningBalanceApplier(IOpeningBalanceRepository openingBalanceRepository, ICurrentUser currentUser)
{
    public async Task ApplyAsync(string kindCode, Guid contactId, ContactOpeningBalanceRequest? openingBalance, CancellationToken cancellationToken)
    {
        if (openingBalance is null) return;
        if (!OpeningBalanceSideCodes.IsValid(openingBalance.SideCode))
            throw new DomainException(ErrorKind.Validation, "The opening balance side must be DEBIT or CREDIT.");
        if (openingBalance.Amount < 0)
            throw new DomainException(ErrorKind.Validation, "The opening balance cannot be negative.");
        if (!currentUser.HasPermission(PermissionCodes.RestrictedOperations.Accounting.OpeningBalance))
            throw new DomainException(ErrorKind.Forbidden, "You do not have the permission to enter opening balances.", ErrorCodes.PermissionDenied);

        var isDebit = openingBalance.SideCode == OpeningBalanceSideCodes.Debit;
        await openingBalanceRepository.PostAsync(null, new ContactReference(kindCode, contactId), AccountingScope.HeadOfficeStoreId,
                                                 isDebit ? openingBalance.Amount : 0, isDebit ? 0 : openingBalance.Amount,
                                                 openingBalance.AsOfDate, cancellationToken);
    }
}
