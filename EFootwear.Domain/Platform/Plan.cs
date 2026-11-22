namespace EFootwear.Domain.Platform;

/// <summary>
/// A subscription plan: the limits a company on this plan has. Table: platform.plans (platform data, not owned by a company).
/// Written only through the platform.create_plan / update_plan functions; the application reads it through EF Core.
/// </summary>
public sealed class Plan
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int MaxStores { get; set; }
    public int MaxUsers { get; set; }

    /// <summary>JSON text of the plan's feature switches (column features, type jsonb).</summary>
    public string Features { get; set; } = "{}";

    public bool IsActive { get; set; } = true;
}
