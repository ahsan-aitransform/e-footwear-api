namespace EFootwear.BLL.Abstractions;

/// <summary>
/// The current time. Services ask this instead of calling DateTimeOffset.UtcNow, so tests can fix the time.
/// All business timestamps are UTC; the database stores timestamptz.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
