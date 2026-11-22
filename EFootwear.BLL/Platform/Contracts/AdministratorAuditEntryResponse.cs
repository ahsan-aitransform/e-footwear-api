using System.Text.Json;

namespace EFootwear.BLL.Platform.Contracts;

public sealed record AdministratorAuditEntryResponse(
    Guid Id,
    string AdministratorUsername,
    string ActionCode,
    Guid? CompanyId,
    string? CompanyName,
    JsonElement Detail,
    DateTimeOffset OccurredAt);
