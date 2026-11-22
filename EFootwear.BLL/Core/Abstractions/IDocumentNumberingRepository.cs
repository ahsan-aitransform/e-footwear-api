using EFootwear.BLL.Core.Contracts;

namespace EFootwear.BLL.Core.Abstractions;

/// <summary>core.number_series. Numbers are handed out by core.next_number; this only reads and moves the start of a series forward.</summary>
public interface IDocumentNumberingRepository
{
    Task<IReadOnlyList<DocumentNumberSeriesResponse>> ListForStoreAsync(Guid companyId, Guid storeId, CancellationToken cancellationToken);

    Task<long?> GetNextValueAsync(Guid companyId, Guid storeId, string seriesCode, DateOnly periodKey, CancellationToken cancellationToken);

    Task SetNextValueAsync(Guid companyId, Guid storeId, string seriesCode, DateOnly periodKey, long nextValue,
                           CancellationToken cancellationToken);
}
