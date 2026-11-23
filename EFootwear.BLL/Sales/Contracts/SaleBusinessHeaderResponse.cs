namespace EFootwear.BLL.Sales.Contracts;

/// <summary>The business details printed at the top of a receipt, as the store really uses them (company settings with the own overrides of the store).</summary>
public sealed record SaleBusinessHeaderResponse(
    string BusinessName,
    string? BusinessNameUrdu,
    string StoreName,
    string? PhoneNumber,
    string? MobileNumber,
    string? Email,
    string? AddressEnglish,
    string? AddressUrdu,
    string? InvoiceText,
    bool HasLogo);
