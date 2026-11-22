namespace EFootwear.BLL.Security.Contracts;

/// <summary>
/// One line of Users Activity (docx 1.4: user, counter, activity, remarks, date and time) with the before and after values of a
/// data change as JSON text, when there are any.
/// </summary>
public sealed record UserActivityEntryResponse(
    Guid EntryId, DateTimeOffset OccurredAt, Guid? UserId, string? UserFullName, string? UserEmail, string EntryKind,
    string Action, string EntityType, string? EntityId, string? Remarks, string? DeviceLabel, string? IpAddress,
    string? OldValuesJson, string? NewValuesJson);
