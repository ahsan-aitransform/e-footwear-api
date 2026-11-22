namespace EFootwear.BLL.Core.Contracts;

public sealed record CompanyProfileResponse(
    Guid Id,
    string Name,
    string? LegalName,
    string? Ntn,
    string? Strn,
    string Timezone,
    string CurrencyCode,
    string RoundingMode,
    string PriceBandBasis,
    string? InvoiceText,
    string PlanCode,
    string Status,
    long RowVersion);
