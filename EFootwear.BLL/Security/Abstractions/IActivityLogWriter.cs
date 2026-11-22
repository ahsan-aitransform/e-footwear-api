using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>Writes one event to the append-only activity log, as the signed-in user, inside the current transaction.</summary>
public interface IActivityLogWriter
{
    Task WriteAsync(ActivityLogEntryRecord entry, CancellationToken cancellationToken);
}
