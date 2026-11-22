using EFootwear.Domain.Common;

namespace EFootwear.Domain.Platform;

/// <summary>A customer company (tenant). Table: platform.companies. Owned by the platform, not by any company.</summary>
public sealed class Company : IHasRowVersion
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? LegalName { get; set; }
    public string? Ntn { get; set; }
    public string? Strn { get; set; }
    public string Timezone { get; set; } = "Asia/Karachi";
    public string CurrencyCode { get; set; } = "PKR";
    public string RoundingMode { get; set; } = RoundingModeCodes.Floor;
    public string PriceBandBasis { get; set; } = PriceBandBasisCodes.ListPrice;
    public string PlanCode { get; set; } = "STANDARD";
    public string? InvoiceTerms { get; set; }
    public string Status { get; set; } = CompanyStatusCodes.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public long RowVersion { get; set; }
}
