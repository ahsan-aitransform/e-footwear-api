namespace EFootwear.BLL.Security.Contracts;

/// <summary>
/// An event the application writes to the activity log itself (the database trigger already logs single-row changes).
/// <paramref name="OldValues"/> and <paramref name="NewValues"/> are serialized to JSON; never put a password, token or hash in them.
/// </summary>
public sealed record ActivityLogEntryRecord(Guid CompanyId, string Action, string EntityType, string? EntityId,
                                            object? OldValues, object? NewValues, string? Remarks);
