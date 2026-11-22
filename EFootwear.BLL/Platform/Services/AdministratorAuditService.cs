using EFootwear.BLL.Common;
using EFootwear.BLL.Platform.Abstractions;
using EFootwear.BLL.Platform.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Platform;

namespace EFootwear.BLL.Platform.Services;

/// <summary>The platform audit log: what platform administrators did, filterable by company, action and dates.</summary>
public sealed class AdministratorAuditService(IAdministratorAuditRepository administratorAuditRepository, RequestContext requestContext)
{
    public async Task<PagedResult<AdministratorAuditEntryResponse>> ListAsync(
        AdministratorAuditQuery query, PageRequest pageRequest, CancellationToken cancellationToken)
    {
        if (!requestContext.IsPlatformAdministrator)
            throw new DomainException(ErrorKind.Forbidden, "Only platform administrators can do this.");
        var actionCode = string.IsNullOrWhiteSpace(query.ActionCode) ? null : query.ActionCode.Trim().ToUpperInvariant();
        if (actionCode is not null && !AdministratorActionCodes.All.Contains(actionCode))
            throw new DomainException(ErrorKind.Validation, $"Unknown action {actionCode}.");
        if (query.From is { } from && query.To is { } to && from > to)
            throw new DomainException(ErrorKind.Validation, "The start date must not be after the end date.");
        return await administratorAuditRepository.ListAsync(query with { ActionCode = actionCode }, pageRequest.Normalized(), cancellationToken);
    }
}
