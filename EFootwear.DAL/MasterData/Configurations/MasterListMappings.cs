using EFootwear.DAL.Persistence;
using EFootwear.Domain.MasterData;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.MasterData.Configurations;

/// <summary>
/// Maps <see cref="MasterListItem"/> to each of the ten master tables as a shared-type entity (same class, one table per list).
/// Read and write a list through <c>databaseContext.Set&lt;MasterListItem&gt;(definition.EntityName)</c>.
/// The Urdu name column is called name_ur in the schema; the audit columns that the database fills (created_at, updated_at,
/// updated_by) are not mapped, so they keep their defaults and triggers.
/// </summary>
public static class MasterListMappings
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var definition in MasterListDefinitions.All)
        {
            modelBuilder.SharedTypeEntity<MasterListItem>(definition.EntityName, builder =>
            {
                builder.ToTable(definition.TableName, MasterListDefinition.SchemaName);
                builder.HasKey(item => new { item.CompanyId, item.Id });
                builder.Property(item => item.Id).ValueGeneratedOnAdd().HasDefaultValueSql("uuidv7()");
                builder.Property(item => item.NameUrdu).HasColumnName("name_ur");
                builder.ConfigureRowVersionConcurrencyToken();
            });
        }
    }
}
