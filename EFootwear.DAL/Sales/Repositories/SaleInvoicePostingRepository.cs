using System.Text.Json;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Sales.Abstractions;
using EFootwear.BLL.Sales.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using EFootwear.Domain.Security.Permissions;
using EFootwear.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EFootwear.DAL.Sales.Repositories;

/// <summary>
/// Writes of the Sales module. Each method calls one database function of migration 0500; the request goes in as JSON because the functions price every
/// line themselves (shelf price, promotion, permission for a price change or a discount) and take the stock out in a fixed order. A refusal of a permission
/// inside the function is turned back into a "forbidden" answer with the readable message the function raised.
/// </summary>
public sealed class SaleInvoicePostingRepository(DatabaseSession databaseSession, ICurrentUser currentUser) : ISaleInvoicePostingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> PostSaleAsync(CreateSaleInvoiceRequest request, Guid? replacedInvoiceId, string? replaceReason, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var linesJson = SaleLinesToJson(request.Lines);
        var paymentsJson = PaymentsToJson(request.Payments);
        var priceChangePermission = PermissionCodes.Invoice.NewSale.ChangeSellingPrice;
        var discountPermission = PermissionCodes.Invoice.NewSale.GiveDiscount;

        return await RunAsync(async () => await databaseContext.Database.SqlQuery<Guid>($"""
            select sales.post_sale_invoice({companyId}, {request.StoreId}, {request.SaleDate}, {request.ClientTransactionId},
                                           {request.CustomerId}, {request.SalesmanStaffId}, {request.WalkInCustomerName}, {request.WalkInCustomerMobile},
                                           {request.PosTerminalDeviceId}, {request.Description}, cast({linesJson} as jsonb), cast({paymentsJson} as jsonb),
                                           {request.CashReturnedAmount}, {priceChangePermission}, {discountPermission}, {replacedInvoiceId}, {replaceReason},
                                           {false}) as "Value"
            """).SingleAsync(cancellationToken));
    }

    public async Task<Guid> PostReturnAsync(CreateSaleReturnRequest request, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var linesJson = ReturnedLinesToJson(request.Lines);

        return await RunAsync(async () => await databaseContext.Database.SqlQuery<Guid>($"""
            select sales.post_return_invoice({companyId}, {request.StoreId}, {request.ReturnDate}, {request.ClientTransactionId}, {request.CustomerId},
                                             {request.Description}, cast({linesJson} as jsonb)) as "Value"
            """).SingleAsync(cancellationToken));
    }

    public async Task<(Guid ReturnInvoiceId, Guid SaleInvoiceId)> PostExchangeAsync(CreateExchangeRequest request, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        var returnedLinesJson = ReturnedLinesToJson(request.ReturnedLines);
        var newSaleLinesJson = SaleLinesToJson(request.NewSaleLines);
        var paymentsJson = PaymentsToJson(request.Payments);

        var row = await RunAsync(async () => await databaseContext.Database.SqlQuery<ExchangeRow>($"""
            select return_invoice_id as "ReturnInvoiceId", sale_invoice_id as "SaleInvoiceId"
              from sales.post_exchange({companyId}, {request.StoreId}, {request.ExchangeDate}, {request.ClientTransactionId}, {request.CustomerId},
                                       {request.SalesmanStaffId}, {request.WalkInCustomerName}, {request.WalkInCustomerMobile}, {request.PosTerminalDeviceId},
                                       {request.Description}, cast({returnedLinesJson} as jsonb), cast({newSaleLinesJson} as jsonb),
                                       cast({paymentsJson} as jsonb), {request.CashReturnedAmount})
            """).SingleAsync(cancellationToken));
        return (row.ReturnInvoiceId, row.SaleInvoiceId);
    }

    public async Task<int> VoidAsync(Guid invoiceId, string reason, CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await RunAsync(async () => await databaseContext.Database.SqlQuery<int>($"""
            select sales.void_invoice({companyId}, {invoiceId}, {reason}) as "Value"
            """).SingleAsync(cancellationToken));
    }

    // The functions raise insufficient_privilege with a readable sentence when the user lacks a permission (for example to change a price).
    // The generic mapping would answer a bare "Not allowed", so the sentence is carried over.
    private static async Task<T> RunAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege && !exception.MessageText.Contains("row-level security"))
        {
            throw new DomainException(ErrorKind.Forbidden, exception.MessageText);
        }
    }

    private static string SaleLinesToJson(IReadOnlyList<SaleLineRequest> lines) => JsonSerializer.Serialize(lines.Select(line => new
    {
        variantId = line.VariantId,
        quantity = line.Quantity,
        unitPrice = line.UnitPrice,
        discountType = line.DiscountTypeCode ?? SaleLineDiscountTypeCodes.None,
        discountValue = line.DiscountValue,
    }), JsonOptions);

    private static string PaymentsToJson(IReadOnlyList<SalePaymentRequest> payments) => JsonSerializer.Serialize(payments.Select(payment => new
    {
        method = payment.PaymentMethodCode,
        paymentAccountId = payment.PaymentAccountId,
        amount = payment.Amount,
        reference = payment.ReferenceNumber,
    }), JsonOptions);

    private static string ReturnedLinesToJson(IReadOnlyList<ReturnedLineRequest> lines) => JsonSerializer.Serialize(lines.Select(line => new
    {
        originalLineId = line.OriginalLineId,
        quantity = line.Quantity,
        reason = line.ReasonText,
        itemCondition = line.ItemConditionCode ?? ReturnedItemConditionCodes.Sellable,
    }), JsonOptions);

    private sealed class ExchangeRow
    {
        public Guid ReturnInvoiceId { get; set; }
        public Guid SaleInvoiceId { get; set; }
    }
}
