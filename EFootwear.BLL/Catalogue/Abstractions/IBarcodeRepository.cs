using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;

namespace EFootwear.BLL.Catalogue.Abstractions;

/// <summary>Barcode lookups, the replacement itself and its history. Implemented in EFootwear.DAL/Catalogue/Repositories/BarcodeRepository.cs.</summary>
public interface IBarcodeRepository
{
    /// <summary>The sizes that currently carry these barcodes (barcodes that no size carries are simply missing from the answer).</summary>
    Task<IReadOnlyList<VariantBarcodeOwner>> FindOwnersAsync(IReadOnlyCollection<string> barcodes, CancellationToken cancellationToken);

    /// <summary>Of these barcodes, the ones used by a size or a deal bundle.</summary>
    Task<IReadOnlyList<string>> FindBarcodesInUseAsync(IReadOnlyCollection<string> barcodes, CancellationToken cancellationToken);

    Task<bool> BatchExistsAsync(Guid batchId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the barcodes in two steps inside one transaction (a unique index forbids two sizes having one barcode even for an instant, so a swap
    /// goes through temporary values) and appends the history rows.
    /// </summary>
    Task ReplaceAsync(Guid batchId, IReadOnlyList<BarcodeHistoryWriteData> replacements, Guid changedBy, CancellationToken cancellationToken);

    Task<PagedResult<BarcodeHistoryEntryResponse>> ListHistoryAsync(string? barcode, Guid? variantId, PageRequest pageRequest, CancellationToken cancellationToken);
    Task AddHistoryAsync(IReadOnlyList<BarcodeHistoryWriteData> entries, Guid changedBy, CancellationToken cancellationToken);
}
