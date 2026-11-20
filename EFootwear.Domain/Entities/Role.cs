namespace EFootwear.Domain.Entities;

/// <summary>A role template. Table: sec.roles. Seeded by the schema; not created by users.</summary>
public sealed class Role
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}
