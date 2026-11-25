using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Abstractions;
using EFootwear.BLL.Customers.Abstractions;
using EFootwear.BLL.Customers.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;

namespace EFootwear.BLL.Customers.Services;

/// <summary>
/// Customer Account Detail: the journal lines of one customer, or of all customers, between two dates, with a running balance and the totals of the
/// period. Read only: there is no way to edit or delete a ledger entry here, because posted entries are never changed (a correction is a
/// reversal posted by the document that made the entry).
/// </summary>
public sealed class CustomerLedgerService(IContactLedgerRepository contactLedgerRepository, ICustomerRepository customerRepository)
{
    public async Task<CustomerLedgerResponse> GetLedgerAsync(CustomerLedgerQuery query, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (query.From is { } from && query.To is { } to && from > to)
            throw new DomainException(ErrorKind.Validation, "The From date cannot be after the To date.");
        if (query.CustomerId is { } customerId && await customerRepository.FindDetailAsync(customerId, cancellationToken) is null)
            throw new DomainException(ErrorKind.NotFound, "The customer was not found.");

        var normalizedPageRequest = pageRequest.Normalized();
        var data = await contactLedgerRepository.GetLedgerAsync(ContactKindCodes.Customer, query.CustomerId, query.From, query.To, normalizedPageRequest, cancellationToken);
        var lines = data.Lines.Select(line => new CustomerLedgerLineResponse(line.LineId, line.ContactId, line.ContactCode, line.ContactName, line.EntryDate,
                                                                          line.SourceType, line.DocumentNumber, line.Description, line.DebitAmount,
                                                                          line.CreditAmount, line.RunningBalanceAmount)).ToList();
        return new CustomerLedgerResponse(query.CustomerId, data.OpeningBalanceAmount,
                                         new PagedResult<CustomerLedgerLineResponse>(lines, normalizedPageRequest.PageNumber, normalizedPageRequest.PageSize, data.LineCount),
                                         data.TotalDebitAmount, data.TotalCreditAmount);
    }
}
