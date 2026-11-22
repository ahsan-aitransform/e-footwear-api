using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EFootwear.DAL.Persistence;

/// <summary>Shared mapping helpers for entity configurations.</summary>
public static class RowVersionConfigurationExtensions
{
    public const string RowVersionColumnName = "row_ver";

    /// <summary>
    /// Maps <see cref="IHasRowVersion.RowVersion"/> to the <c>row_ver</c> column as an optimistic-concurrency token.
    /// The database trigger core.touch_row increments the value on every UPDATE and EF Core reads the new value back,
    /// so two people editing the same record cannot silently overwrite each other: the second save fails with a
    /// DbUpdateConcurrencyException, which the API turns into 409 CONCURRENT_UPDATE.
    /// Call it in the configuration of every entity whose table has row_ver.
    /// </summary>
    public static EntityTypeBuilder<TEntity> ConfigureRowVersionConcurrencyToken<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IHasRowVersion
    {
        builder.Property(entity => entity.RowVersion)
            .HasColumnName(RowVersionColumnName)
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();
        return builder;
    }
}
