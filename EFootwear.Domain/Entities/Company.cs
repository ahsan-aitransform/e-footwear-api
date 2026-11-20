namespace EFootwear.Domain.Entities;

/// <summary>A customer company (tenant). Table: platform.companies. Owned by the platform, not by any company.</summary>
public sealed class Company
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Timezone { get; set; } = "Asia/Karachi";
    public string CurrencyCode { get; set; } = "PKR";
    public string Status { get; set; } = "ACTIVE";   // ACTIVE, SUSPENDED, CLOSED
    public DateTimeOffset CreatedAt { get; set; }
}
