namespace EFootwear.Application.Abstractions;

public sealed record InvoiceHeaderInsert(long CompanyId, long StoreId, long ChannelId, string DocType, long InvoiceNo, DateOnly SaleDate, int? DailyBillNo,
                                         long? DeviceId, Guid? DeviceSaleUuid, DateTimeOffset OccurredAt, long? OriginalInvoiceId, long? CustomerId,
                                         long? SalesmanId, string? WalkinName, string? WalkinMobile, string? OnlineOrderRef);

public sealed record InvoiceLineInsert(long CompanyId, long InvoiceId, int LineNo, long StoreId, long ChannelId, DateOnly SaleDate, string DocType, long VariantId,
                                       decimal Qty, long ListPrice, long UnitPrice, string DiscountSource, long? RuleId, long? ConflictGroupId, long? BundleId,
                                       long? ManualApprovedBy, long DiscountAmount, decimal TaxRate, long Taxable, long Tax, long Net, decimal UnitCost, long CostAmount,
                                       long? SalesmanId, long? OriginalLineId, DateOnly? OriginalLineSaleDate);

public sealed record InvoiceTotalsUpdate(long Gross, long LineDiscount, long BillDiscount, long Taxable, long Tax, long Net, long Paid, long Change, long Credit, long Cost,
                                         long? RelatedInvoiceId = null);

public sealed record PaymentInsert(long CompanyId, long InvoiceId, long StoreId, DateOnly SaleDate, string Method, long AccountId, long Amount, string? Reference);

public interface ISalesRepository
{
    Task<Row?> FindBySaleUuidAsync(Guid uuid, CancellationToken ct);
    Task<Row?> VariantSnapshotAsync(long variantId, long storeId, CancellationToken ct);
    Task<long> NextNumberAsync(long companyId, long storeId, string series, DateOnly? period, CancellationToken ct);
    Task<long> InsertHeaderAsync(InvoiceHeaderInsert h, CancellationToken ct);
    Task<long> InsertLineAsync(InvoiceLineInsert l, CancellationToken ct);
    Task UpdateTotalsAsync(long invoiceId, InvoiceTotalsUpdate t, CancellationToken ct);
    Task InsertPaymentAsync(PaymentInsert p, CancellationToken ct);
    Task<long> RoleAccountAsync(string role, CancellationToken ct);
    Task<long?> WalkinPartyAsync(CancellationToken ct);
    Task<Row?> PartyBriefAsync(long partyId, CancellationToken ct);
    Task<Row?> InvoiceAsync(long id, CancellationToken ct);
    Task<List<Row>> InvoiceLinesAsync(long invoiceId, CancellationToken ct);
    Task<List<Row>> InvoicePaymentsAsync(long invoiceId, CancellationToken ct);
    Task<Page<Row>> InvoicesAsync(SalesFilter f, PageQuery q, CancellationToken ct);
    Task<List<Row>> ReturnableLinesAsync(long invoiceId, CancellationToken ct);
    Task<decimal> ReturnedQtyAsync(long originalLineId, CancellationToken ct);
    Task<long> LiveReturnCountAsync(long invoiceId, CancellationToken ct);
    Task VoidAsync(long invoiceId, string reason, long userId, CancellationToken ct);
    Task<List<Row>> OpenDriftsAsync(CancellationToken ct);
}

public sealed record SalesFilter(long? StoreId, DateOnly? From, DateOnly? To, string? DocType, string? Status, long? CustomerId, long? SalesmanId, long? InvoiceNo);

public sealed record PurchaseHeaderInsert(long CompanyId, long StoreId, string DocType, long InvoiceNo, string? VendorBillNo, long VendorId, DateOnly BillDate,
                                          DateOnly? ReceivedOn, DateOnly? DueDate, long Gross, long Discount, long Tax, long Net);

public sealed record PurchaseLineInsert(long CompanyId, long InvoiceId, int LineNo, long VariantId, decimal Qty, long UnitCost, long SalePrice, decimal? SaleDiscountPct,
                                        long LineAmount, bool UpdatesSalePrice, long? OriginalLineId);

public sealed record PurchaseFilter(long? StoreId, DateOnly? From, DateOnly? To, long? VendorId, string? DocType, string? Status, string? Search);

public interface IPurchaseRepository
{
    Task<long> NextNumberAsync(long companyId, long storeId, string series, CancellationToken ct);
    Task<long> InsertHeaderAsync(PurchaseHeaderInsert h, CancellationToken ct);
    Task<long> InsertLineAsync(PurchaseLineInsert l, CancellationToken ct);
    Task<Row?> VendorAsync(long id, CancellationToken ct);
    Task<Row?> InvoiceAsync(long id, CancellationToken ct);
    Task<List<Row>> InvoiceLinesAsync(long invoiceId, CancellationToken ct);
    Task<Page<Row>> InvoicesAsync(PurchaseFilter f, PageQuery q, CancellationToken ct);
    Task<Row?> LineAsync(long lineId, CancellationToken ct);
    Task<long> LiveReturnCountAsync(long invoiceId, CancellationToken ct);
    Task VoidAsync(long invoiceId, string reason, long userId, CancellationToken ct);
    Task<Row?> VariantAsync(long variantId, CancellationToken ct);
    Task<List<Row>> VendorBillsAsync(long vendorId, CancellationToken ct);
}

public interface IStockMoves
{
    Task<decimal> PostMoveAsync(long companyId, long storeId, long variantId, decimal qty, string docType, long docId, decimal unitCost, DateOnly date, CancellationToken ct);
    Task<Dictionary<long, decimal>> OnHandAsync(long storeId, IReadOnlyCollection<long> variantIds, CancellationToken ct);
}
