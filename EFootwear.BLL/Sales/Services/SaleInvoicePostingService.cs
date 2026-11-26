using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Core.Services;
using EFootwear.BLL.Customers.Services;
using EFootwear.BLL.Sales.Abstractions;
using EFootwear.BLL.Sales.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Sales;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Sales.Services;

/// <summary>
/// New Sale, Sale Return, Exchange Invoice and void (Part 11, Part 12; docx 5.1 to 5.3 and "Delete" of 5.13). One request is one transaction: the service
/// checks the request, calls one database function that numbers the document, moves the stock and writes the lines, and the baseline triggers post the
/// journal when the transaction commits. A posted invoice is never edited: a correction is a void plus a new invoice, in the same transaction.
/// Business policies stay open and are not enforced here: return periods, refunds in cash, tax, who may edit or void and until when.
/// </summary>
public sealed class SaleInvoicePostingService(
    ISaleInvoicePostingRepository postingRepository,
    ISaleInvoiceReadRepository readRepository,
    CompanySettingsService companySettingsService,
    CustomerService customerService,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<SaleInvoicePostingService> logger)
{
    public async Task<SaleInvoiceResponse> CreateSaleAsync(CreateSaleInvoiceRequest request, CancellationToken cancellationToken)
    {
        var cleanedRequest = await SaveTypedCustomerAsync(await ValidateAndCleanSaleAsync(request, cancellationToken), cancellationToken);
        var invoiceId = await postingRepository.PostSaleAsync(cleanedRequest, replacedInvoiceId: null, replaceReason: null, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var invoice = await GetPostedInvoiceAsync(invoiceId, cancellationToken);
        logger.LogInformation("Sale invoice {InvoiceNumber} ({InvoiceId}) of {NetAmount} rupees posted in store {StoreId} of company {CompanyId} by user {UserId}",
            invoice.InvoiceNumber, invoice.Id, invoice.NetAmount, invoice.StoreId, currentUser.CompanyId, currentUser.UserId);
        return invoice;
    }

    public async Task<SaleInvoiceResponse> CreateReturnAsync(CreateSaleReturnRequest request, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(request.StoreId);
        SaleRequestValidator.EnsureSaleHeaderIsWellFormed(request.ClientTransactionId, request.ReturnDate, request.Description, null, null);
        SaleRequestValidator.EnsureReturnedLinesAreWellFormed(request.Lines);
        await EnsureReturnedQuantitiesAreAvailableAsync(request.Lines, cancellationToken);

        var cleanedRequest = request with
        {
            Description = SaleRules.CleanOptionalText(request.Description, SaleRules.DescriptionMaximumLength, "The description"),
            Lines = CleanReturnedLines(request.Lines),
        };
        var invoiceId = await postingRepository.PostReturnAsync(cleanedRequest, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var invoice = await GetPostedInvoiceAsync(invoiceId, cancellationToken);
        logger.LogInformation("Return invoice {InvoiceNumber} ({InvoiceId}) of {NetAmount} rupees posted in store {StoreId} of company {CompanyId} by user {UserId}",
            invoice.InvoiceNumber, invoice.Id, invoice.NetAmount, invoice.StoreId, currentUser.CompanyId, currentUser.UserId);
        return invoice;
    }

    public async Task<ExchangeResponse> CreateExchangeAsync(CreateExchangeRequest request, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(request.StoreId);
        SaleRequestValidator.EnsureSaleHeaderIsWellFormed(request.ClientTransactionId, request.ExchangeDate, request.Description,
            request.WalkInCustomerName, request.WalkInCustomerMobile);
        SaleRequestValidator.EnsureReturnedLinesAreWellFormed(request.ReturnedLines);
        SaleRequestValidator.EnsureSaleLinesAreWellFormed(request.NewSaleLines, "exchange");
        SaleRequestValidator.EnsurePaymentsAreWellFormed(request.Payments, request.CashReturnedAmount, await EnabledPaymentMethodCodesAsync(cancellationToken));
        await EnsureReturnedQuantitiesAreAvailableAsync(request.ReturnedLines, cancellationToken);

        var cleanedRequest = request with
        {
            Description = SaleRules.CleanOptionalText(request.Description, SaleRules.DescriptionMaximumLength, "The description"),
            WalkInCustomerName = SaleRules.CleanOptionalText(request.WalkInCustomerName, SaleRules.WalkInNameMaximumLength, "The customer name"),
            WalkInCustomerMobile = SaleRules.CleanOptionalText(request.WalkInCustomerMobile, SaleRules.WalkInMobileMaximumLength, "The mobile number"),
            ReturnedLines = CleanReturnedLines(request.ReturnedLines),
        };
        var (returnInvoiceId, saleInvoiceId) = await postingRepository.PostExchangeAsync(cleanedRequest, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var returnInvoice = await GetPostedInvoiceAsync(returnInvoiceId, cancellationToken);
        var saleInvoice = await GetPostedInvoiceAsync(saleInvoiceId, cancellationToken);
        logger.LogInformation("Exchange posted in store {StoreId} of company {CompanyId} by user {UserId}: return invoice {ReturnInvoiceNumber}, sale invoice {SaleInvoiceNumber}",
            request.StoreId, currentUser.CompanyId, currentUser.UserId, returnInvoice.InvoiceNumber, saleInvoice.InvoiceNumber);
        return new ExchangeResponse(returnInvoice, saleInvoice);
    }

    public async Task<VoidSaleInvoiceResponse> VoidAsync(Guid invoiceId, VoidSaleInvoiceRequest request, CancellationToken cancellationToken)
    {
        var reason = SaleRules.CleanRequiredReason(request.Reason, "the reason for voiding the invoice");
        var invoice = await readRepository.FindInvoiceAsync(invoiceId, cancellationToken)
                      ?? throw new DomainException(ErrorKind.NotFound, "The invoice was not found.");
        EnsureStoreAccess(invoice.StoreId);
        if (invoice.StatusCode == SaleInvoiceStatusCodes.Void)
            throw new DomainException(ErrorKind.Conflict, "This invoice is already void.");

        var voidedCount = await postingRepository.VoidAsync(invoiceId, reason, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("{DocumentType} invoice {InvoiceNumber} ({InvoiceId}) voided in store {StoreId} of company {CompanyId} by user {UserId}: {Reason}",
            invoice.DocumentTypeCode, invoice.InvoiceNumber, invoiceId, invoice.StoreId, currentUser.CompanyId, currentUser.UserId, reason);
        return new VoidSaleInvoiceResponse(await GetPostedInvoiceAsync(invoiceId, cancellationToken), voidedCount);
    }

    /// <summary>
    /// "Edit" of a posted sale: void it with a reason and post the corrected sale in the same transaction. Only a plain sale can be corrected this way; the two
    /// halves of an exchange are voided together and entered again from the Exchange screen.
    /// </summary>
    public async Task<VoidAndReenterSaleResponse> VoidAndReenterAsync(Guid invoiceId, VoidAndReenterSaleRequest request, CancellationToken cancellationToken)
    {
        var reason = SaleRules.CleanRequiredReason(request.VoidReason, "the reason for correcting the invoice");
        var original = await readRepository.FindInvoiceAsync(invoiceId, cancellationToken)
                       ?? throw new DomainException(ErrorKind.NotFound, "The invoice was not found.");
        EnsureStoreAccess(original.StoreId);
        if (original.DocumentTypeCode != SaleDocumentTypeCodes.Sale)
            throw new DomainException(ErrorKind.Validation, "Only a sale invoice can be corrected this way. Void a return and enter it again.");
        if (original.StatusCode == SaleInvoiceStatusCodes.Void)
            throw new DomainException(ErrorKind.Conflict, "This invoice is already void.");
        if (original.RelatedInvoiceId is not null)
            throw new DomainException(ErrorKind.Validation, "This sale is half of an exchange. Void the exchange and enter it again from the Exchange screen.");
        if (request.CorrectedSale.StoreId != original.StoreId)
            throw new DomainException(ErrorKind.Validation, "The corrected sale must be made in the same store as the invoice it replaces.");

        var cleanedSale = await SaveTypedCustomerAsync(await ValidateAndCleanSaleAsync(request.CorrectedSale, cancellationToken), cancellationToken);
        var newInvoiceId = await postingRepository.PostSaleAsync(cleanedSale, invoiceId, reason, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var corrected = await GetPostedInvoiceAsync(newInvoiceId, cancellationToken);
        logger.LogInformation("Sale invoice {OldInvoiceNumber} ({OldInvoiceId}) voided and entered again as {NewInvoiceNumber} ({NewInvoiceId}) in store {StoreId} of company {CompanyId} by user {UserId}: {Reason}",
            original.InvoiceNumber, invoiceId, corrected.InvoiceNumber, corrected.Id, original.StoreId, currentUser.CompanyId, currentUser.UserId, reason);
        return new VoidAndReenterSaleResponse(await GetPostedInvoiceAsync(invoiceId, cancellationToken), corrected);
    }

    private async Task<CreateSaleInvoiceRequest> ValidateAndCleanSaleAsync(CreateSaleInvoiceRequest request, CancellationToken cancellationToken)
    {
        EnsureStoreAccess(request.StoreId);
        SaleRequestValidator.EnsureSaleHeaderIsWellFormed(request.ClientTransactionId, request.SaleDate, request.Description,
            request.WalkInCustomerName, request.WalkInCustomerMobile);
        SaleRequestValidator.EnsureSaleLinesAreWellFormed(request.Lines);
        SaleRequestValidator.EnsurePaymentsAreWellFormed(request.Payments, request.CashReturnedAmount, await EnabledPaymentMethodCodesAsync(cancellationToken));
        return request with
        {
            Description = SaleRules.CleanOptionalText(request.Description, SaleRules.DescriptionMaximumLength, "The description"),
            WalkInCustomerName = SaleRules.CleanOptionalText(request.WalkInCustomerName, SaleRules.WalkInNameMaximumLength, "The customer name"),
            WalkInCustomerMobile = SaleRules.CleanOptionalText(request.WalkInCustomerMobile, SaleRules.WalkInMobileMaximumLength, "The mobile number"),
        };
    }

    /// <summary>
    /// A sale with a typed name AND mobile number but no chosen customer belongs to that customer, who is saved if new (in the same transaction as
    /// the sale). With only one of the two there is not enough for a customer: the sale stays a cash sale that keeps the typed text.
    /// </summary>
    private async Task<CreateSaleInvoiceRequest> SaveTypedCustomerAsync(CreateSaleInvoiceRequest request, CancellationToken cancellationToken)
    {
        if (request.CustomerId is not null || string.IsNullOrWhiteSpace(request.WalkInCustomerName) || string.IsNullOrWhiteSpace(request.WalkInCustomerMobile))
            return request;
        var customerId = await customerService.FindOrAddForSaleAsync(request.WalkInCustomerName, request.WalkInCustomerMobile, cancellationToken);
        return request with { CustomerId = customerId, WalkInCustomerName = null, WalkInCustomerMobile = null };
    }

    /// <summary>
    /// Names the line when a return asks for more than is left to return. The database trigger sales.check_return_quantity stays the authority (it also
    /// covers two people returning the same line at the same moment); this check gives the answer per line before anything is posted.
    /// </summary>
    private async Task EnsureReturnedQuantitiesAreAvailableAsync(IReadOnlyList<ReturnedLineRequest> lines, CancellationToken cancellationToken)
    {
        var returnable = (await readRepository.FindReturnableLinesAsync(lines.Select(line => line.OriginalLineId).Distinct().ToList(), cancellationToken))
            .ToDictionary(line => line.LineId);

        for (var index = 0; index < lines.Count; index++)
        {
            if (!returnable.ContainsKey(lines[index].OriginalLineId))
                throw new DomainException(ErrorKind.Validation, $"Line {index + 1}: the sale line being returned was not found, or its invoice is void.");
        }

        foreach (var group in lines.Select((line, index) => (line, lineNumber: index + 1)).GroupBy(entry => entry.line.OriginalLineId))
        {
            var sale = returnable[group.Key];
            if (sale.ReturnableQuantity < group.Sum(entry => entry.line.Quantity))
                throw new DomainException(ErrorKind.Validation,
                    $"Line {group.First().lineNumber}: only {sale.ReturnableQuantity:0.###} of {sale.ProductName} (size {sale.SizeCode}) can still be returned " +
                    $"(sold {sale.SoldQuantity:0.###}, already returned {sale.AlreadyReturnedQuantity:0.###}) on invoice {sale.InvoiceNumber}.");
        }
    }

    private static IReadOnlyList<ReturnedLineRequest> CleanReturnedLines(IReadOnlyList<ReturnedLineRequest> lines) =>
        lines.Select(line => line with
        {
            ReasonText = SaleRules.CleanOptionalText(line.ReasonText, SaleRules.ReasonMaximumLength, "The return reason"),
            ItemConditionCode = line.ItemConditionCode ?? ReturnedItemConditionCodes.Sellable,
        }).ToList();

    private async Task<IReadOnlyCollection<string>> EnabledPaymentMethodCodesAsync(CancellationToken cancellationToken) =>
        (await companySettingsService.GetEnabledPaymentMethodsAsync(cancellationToken)).Select(method => method.Code).ToList();

    private async Task<SaleInvoiceResponse> GetPostedInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await readRepository.FindInvoiceAsync(invoiceId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "The invoice was not found after saving.");

    private void EnsureStoreAccess(Guid storeId)
    {
        if (storeId == Guid.Empty || !currentUser.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");
    }
}
