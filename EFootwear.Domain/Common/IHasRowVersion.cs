namespace EFootwear.Domain.Common;

/// <summary>
/// Implemented by entities whose table has a <c>row_ver</c> column. PostgreSQL has no SQL Server style rowversion,
/// so the database trigger core.touch_row increments the number on every UPDATE and EF Core uses it as an
/// optimistic-concurrency token: saving a stale copy fails instead of silently overwriting a newer change.
/// Map it with <c>ConfigureRowVersionConcurrencyToken()</c> in the entity configuration.
/// </summary>
public interface IHasRowVersion
{
    long RowVersion { get; set; }
}
