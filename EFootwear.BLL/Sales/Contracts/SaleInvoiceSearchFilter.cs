namespace EFootwear.BLL.Sales.Contracts;

/// <summary>The filters of View Invoices (and of View Deleted Invoices, which fixes the status). Every filter is optional.</summary>
public sealed record SaleInvoiceSearchFilter(
    Guid? StoreId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    long? InvoiceNumber = null,
    string? DocumentTypeCode = null,
    string? StatusCode = null,
    Guid? CustomerId = null,
    Guid? SalesmanStaffId = null,
    long? MinimumNetAmount = null,
    long? MaximumNetAmount = null);
