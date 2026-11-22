using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.Domain.Core;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Core.Services;

/// <summary>
/// Document Numbering: shows where each document number series of a store continues and moves it forward (never back).
/// Numbers are handed out by core.next_number inside the modules; the series list is the known series plus any other stored one.
/// </summary>
public sealed class DocumentNumberingService(
    IDocumentNumberingRepository documentNumberingRepository,
    StoreAccessGuard storeAccessGuard,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<DocumentNumberingService> logger)
{
    public async Task<IReadOnlyList<DocumentNumberSeriesResponse>> ListAsync(Guid storeId, CancellationToken cancellationToken)
    {
        await storeAccessGuard.EnsureAccessibleAsync(storeId, cancellationToken);
        var stored = await documentNumberingRepository.ListForStoreAsync(requestContext.CompanyId, storeId, cancellationToken);

        var result = new List<DocumentNumberSeriesResponse>();
        foreach (var seriesCode in DocumentNumberSeriesCodes.All)
        {
            var existing = stored.FirstOrDefault(series => series.SeriesCode == seriesCode && series.PeriodKey == DocumentNumberingRules.NeverResetsPeriodKey);
            result.Add(existing ?? new DocumentNumberSeriesResponse(storeId, seriesCode, DocumentNumberingRules.NeverResetsPeriodKey, 1, false));
        }
        result.AddRange(stored.Where(series => !result.Contains(series)));
        return result;
    }

    public async Task SetNextValueAsync(SetDocumentNumberRequest request, CancellationToken cancellationToken)
    {
        await storeAccessGuard.EnsureAccessibleAsync(request.StoreId, cancellationToken);
        var seriesCode = DocumentNumberingRules.EnsureValidSeriesCode(request.SeriesCode);
        var companyId = requestContext.CompanyId;

        var current = await documentNumberingRepository.GetNextValueAsync(
            companyId, request.StoreId, seriesCode, DocumentNumberingRules.NeverResetsPeriodKey, cancellationToken);
        DocumentNumberingRules.EnsureNextValueDoesNotGoBack(request.NextValue, current);

        await documentNumberingRepository.SetNextValueAsync(
            companyId, request.StoreId, seriesCode, DocumentNumberingRules.NeverResetsPeriodKey, request.NextValue, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Series {SeriesCode} of store {StoreId} continues at {NextValue} (set by user {UserId})",
            seriesCode, request.StoreId, request.NextValue, requestContext.UserId);
    }
}
