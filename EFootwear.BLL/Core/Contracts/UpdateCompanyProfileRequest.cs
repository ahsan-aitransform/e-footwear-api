namespace EFootwear.BLL.Core.Contracts;

/// <summary>The legal and regional part of the company profile. The business name and invoice text belong to the Business Info tab.</summary>
public sealed record UpdateCompanyProfileRequest(
    string? LegalName,
    string? Ntn,
    string? Strn,
    string Timezone,
    string CurrencyCode,
    string RoundingMode,
    string PriceBandBasis,
    long? ExpectedRowVersion);
