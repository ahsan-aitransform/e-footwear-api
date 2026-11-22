namespace EFootwear.BLL.Platform.Contracts;

public sealed record AdministratorAuditQuery(Guid? CompanyId, string? ActionCode, DateTimeOffset? From, DateTimeOffset? To);
