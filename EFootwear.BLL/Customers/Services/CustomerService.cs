using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Contacts.Abstractions;
using EFootwear.BLL.Contacts.Services;
using EFootwear.BLL.MasterData.Abstractions;
using EFootwear.BLL.Customers.Abstractions;
using EFootwear.BLL.Customers.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Contacts;
using EFootwear.Domain.Customers;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Customers.Services;

/// <summary>
/// Use cases of New Customer, View Customers and Search Customer. Rules: the name and the phone number are required; the email, the birth date and the
/// city and the CNIC are optional; a customer is regular unless made special; the code is generated; a customer that has accounting history cannot be deleted (it can be made inactive). The balance is
/// never edited here: it comes from the journal. An opening balance is posted through the accounting opening-balance mechanism and needs the
/// OPENING_BALANCE permission. The walk-in customer "Cash Sale" belongs to the system: it cannot be edited or deleted.
/// </summary>
public sealed class CustomerService(
    ICustomerRepository customerRepository,
    IContactCodeGenerator codeGenerator,
    ContactFieldValidator fieldValidator,
    ContactOpeningBalanceApplier openingBalanceApplier,
    IRowUsageRepository rowUsageRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<CustomerService> logger)
{
    public const int LookupMaximumItemCount = 50;
    private const string CustomersTableName = "contacts.customers";

    // The balance table points at every customer that ever had a balance; it must not block a deletion on its own.
    private static readonly HashSet<string> TablesThatDoNotBlockDeletion = ["contacts.customer_balances"];

    public Task<PagedResult<CustomerListItemResponse>> SearchAsync(CustomerSearchRequest filter, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (filter.StatusCode is not null) ContactFieldValidator.EnsureStatusIsValid(filter.StatusCode);
        return customerRepository.SearchAsync(filter, pageRequest.Normalized(), cancellationToken);
    }

    public Task<IReadOnlyList<CustomerLookupItemResponse>> LookupAsync(string? searchText, CancellationToken cancellationToken) =>
        customerRepository.LookupAsync(string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim(), LookupMaximumItemCount, cancellationToken);

    public async Task<CustomerDetailResponse> GetAsync(Guid customerId, CancellationToken cancellationToken) =>
        await customerRepository.FindDetailAsync(customerId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The customer was not found.");

    public async Task<CreatedIdResponse> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var record = await BuildValidatedRecordAsync(request, cancellationToken);
        var code = await codeGenerator.GenerateNextCodeAsync(cancellationToken);
        var customerId = await customerRepository.AddAsync(record, code, currentUser.UserId, cancellationToken);
        await openingBalanceApplier.ApplyAsync(ContactKindCodes.Customer, customerId, request.OpeningBalance, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Customer {CustomerId} ({Code}) created in company {CompanyId} by user {UserId}", customerId, code, currentUser.CompanyId, currentUser.UserId);
        return new CreatedIdResponse(customerId);
    }

    /// <summary>
    /// The customer whose name and phone number were typed on a sale: the one already saved with both, or else a new regular customer. The same
    /// rules as New Customer apply to the two fields. It does not commit: the customer is stored only together with the sale.
    /// </summary>
    public async Task<Guid> FindOrAddForSaleAsync(string name, string phone, CancellationToken cancellationToken)
    {
        var validName = fieldValidator.RequireName(name, "Customer");
        var validPhone = fieldValidator.RequirePhone(phone, "Customer");
        if (await customerRepository.FindActiveIdByNameAndPhoneAsync(validName, validPhone, cancellationToken) is { } existingId)
            return existingId;

        var record = new CustomerWriteRecord(validName, validPhone, null, null, null, ContactStatusCodes.Active, null, null);
        var code = await codeGenerator.GenerateNextCodeAsync(cancellationToken);
        var customerId = await customerRepository.AddAsync(record, code, currentUser.UserId, cancellationToken);
        logger.LogInformation("Customer {CustomerId} ({Code}) added from a sale in company {CompanyId} by user {UserId}", customerId, code, currentUser.CompanyId, currentUser.UserId);
        return customerId;
    }

    public async Task UpdateAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(customerId, cancellationToken);
        EnsureIsNotTheWalkInCustomer(existing, "edited");
        var record = await BuildValidatedRecordAsync(request, cancellationToken);
        await customerRepository.UpdateAsync(customerId, record, request.RowVersion, cancellationToken);
        await openingBalanceApplier.ApplyAsync(ContactKindCodes.Customer, customerId, request.OpeningBalance, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(customerId, cancellationToken);
        EnsureIsNotTheWalkInCustomer(existing, "deleted");
        var usages = (await rowUsageRepository.FindUsagesAsync(CustomersTableName, customerId, cancellationToken))
            .Where(usage => !TablesThatDoNotBlockDeletion.Contains(usage.TableName))
            .ToList();
        RowUsageMessage.ThrowWhenUsed($"Customer {existing.Code} {existing.Name}", usages);

        await customerRepository.SoftDeleteAsync(customerId, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Customer {CustomerId} deleted in company {CompanyId} by user {UserId}", customerId, currentUser.CompanyId, currentUser.UserId);
    }

    private static void EnsureIsNotTheWalkInCustomer(CustomerDetailResponse customer, string action)
    {
        if (customer.Code == CustomerRules.WalkInCustomerCode)
            throw new DomainException(ErrorKind.Validation, $"The walk-in customer {customer.Name} belongs to the system and cannot be {action}.");
    }

    private async Task<CustomerWriteRecord> BuildValidatedRecordAsync(ICustomerEditableFields fields, CancellationToken cancellationToken)
    {
        var name = fieldValidator.RequireName(fields.Name, "Customer");
        var phone = fieldValidator.RequirePhone(fields.Phone, "Customer");
        ContactFieldValidator.EnsureStatusIsValid(fields.StatusCode);
        var email = fieldValidator.ValidateEmail(fields.Email);
        fieldValidator.EnsureBirthDateIsValid(fields.BirthDate);
        await fieldValidator.EnsureCityIsUsableAsync(fields.CityId, cancellationToken);
        var cnic = ValidateCnic(fields.Cnic);
        if (fields.CustomerTypeCode is not null && !CustomerTypeCodes.IsValid(fields.CustomerTypeCode))
            throw new DomainException(ErrorKind.Validation, "The customer type must be REGULAR or SPECIAL.");
        return new CustomerWriteRecord(name, phone, email, fields.BirthDate, fields.CityId, fields.StatusCode, cnic, fields.CustomerTypeCode);
    }

    private static string? ValidateCnic(string? cnic)
    {
        var trimmed = cnic?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (!CustomerRules.IsValidCnic(trimmed))
            throw new DomainException(ErrorKind.Validation, "The CNIC must have 13 digits, for example 35202-1234567-1.");
        return CustomerRules.FormatCnic(trimmed);
    }
}
