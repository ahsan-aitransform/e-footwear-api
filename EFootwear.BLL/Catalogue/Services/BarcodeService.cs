using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Catalogue.Abstractions;
using EFootwear.BLL.Catalogue.Contracts;
using EFootwear.BLL.Common;
using EFootwear.BLL.Common.Spreadsheet;
using EFootwear.Domain.Catalogue;
using EFootwear.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Catalogue.Services;

/// <summary>
/// Replace Barcodes (docx 4.13), the barcode history, and the data for the Generate Bar Codes labels (a page the docx lacks).
///
/// Replace Barcodes: the old system imports old/new barcode pairs from Excel into a grid, then saves. Here: the file is read (column A = old barcode,
/// column B = new barcode; a header row is allowed), every row is checked and shown, and Save applies the list in one transaction with a history row
/// per size. One invalid row refuses the whole list. A new barcode may be a barcode that another row of the same list frees, so chains and swaps work.
/// </summary>
public sealed class BarcodeService(
    IBarcodeRepository barcodeRepository,
    IProductListingRepository productListingRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    ILogger<BarcodeService> logger)
{
    public const int MaximumReplacementRows = 5000;
    public const int MaximumLabelKinds = 500;
    public const int MaximumCopiesPerLabel = 1000;
    public const int MaximumLabelsPerRequest = 5000;
    private const int ProblemsShownInError = 5;

    private static readonly string[] HeaderWords = ["old", "old barcode", "oldbarcode", "old bar code", "barcode", "from"];

    public async Task<BarcodeReplacementPreviewResponse> PreviewFromFileAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        var rows = SpreadsheetReader.ReadRows(content, fileName);
        var firstRowIsHeader = rows.Count > 0 && HeaderWords.Contains(rows[0].FirstOrDefault()?.Trim().ToLowerInvariant() ?? "");
        var pairs = new List<BarcodeReplacementPairRequest>();
        var fileRowNumbers = new List<int>();
        for (var index = firstRowIsHeader ? 1 : 0; index < rows.Count; index++)
        {
            pairs.Add(new BarcodeReplacementPairRequest(rows[index].ElementAtOrDefault(0) ?? "", rows[index].ElementAtOrDefault(1) ?? ""));
            fileRowNumbers.Add(index + 1);
        }
        return await PreviewAsync(pairs, fileRowNumbers, cancellationToken);
    }

    public Task<BarcodeReplacementPreviewResponse> PreviewAsync(IReadOnlyList<BarcodeReplacementPairRequest> pairs, CancellationToken cancellationToken) =>
        PreviewAsync(pairs, null, cancellationToken);

    public async Task<BarcodesReplacedResponse> ReplaceAsync(ReplaceBarcodesRequest request, CancellationToken cancellationToken)
    {
        var batchId = request.ClientTransactionId ?? Guid.NewGuid();
        if (request.ClientTransactionId is not null && await barcodeRepository.BatchExistsAsync(batchId, cancellationToken))
            return new BarcodesReplacedResponse(batchId, 0, true);

        var preview = await PreviewAsync(request.Pairs, cancellationToken);
        if (preview.InvalidRowCount > 0)
        {
            var problems = preview.Rows.Where(row => !row.IsValid).Take(ProblemsShownInError).Select(row => $"row {row.RowNumber}: {row.Problem}");
            throw new DomainException(ErrorKind.Validation, $"The list cannot be applied ({preview.InvalidRowCount} rows have a problem). {string.Join("; ", problems)}");
        }

        var owners = await barcodeRepository.FindOwnersAsync(preview.Rows.Select(row => row.OldBarcode).ToList(), cancellationToken);
        var variantByBarcode = owners.ToDictionary(owner => owner.Barcode, StringComparer.Ordinal);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "REPLACE_BARCODES" : request.Reason.Trim();
        var replacements = preview.Rows
            .Select(row => new BarcodeHistoryWriteData(variantByBarcode[row.OldBarcode].VariantId, row.OldBarcode, row.NewBarcode, batchId, reason)).ToList();

        await barcodeRepository.ReplaceAsync(batchId, replacements, currentUser.UserId, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Barcodes replaced for {ReplacedCount} sizes (batch {BatchId}) in company {CompanyId} by user {UserId}",
            replacements.Count, batchId, currentUser.CompanyId, currentUser.UserId);
        return new BarcodesReplacedResponse(batchId, replacements.Count, false);
    }

    public Task<PagedResult<BarcodeHistoryEntryResponse>> ListHistoryAsync(string? barcode, Guid? variantId, PageRequest pageRequest, CancellationToken cancellationToken) =>
        barcodeRepository.ListHistoryAsync(string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim(), variantId, pageRequest.Normalized(), cancellationToken);

    public async Task<IReadOnlyList<BarcodeLabelResponse>> BuildLabelsAsync(BarcodeLabelRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new DomainException(ErrorKind.Validation, "Choose at least one size to print.");
        if (request.Items.Count > MaximumLabelKinds)
            throw new DomainException(ErrorKind.Validation, $"Choose at most {MaximumLabelKinds} different sizes at a time.");
        if (request.StoreId is { } storeId && (storeId == Guid.Empty || !currentUser.CanAccessStore(storeId)))
            throw new DomainException(ErrorKind.Forbidden, "You cannot work in this store.", ErrorCodes.Forbidden);
        if (request.Items.Any(item => item.Copies < 1 || item.Copies > MaximumCopiesPerLabel))
            throw new DomainException(ErrorKind.Validation, $"The number of labels per size must be between 1 and {MaximumCopiesPerLabel}.");
        if (request.Items.Sum(item => (long)item.Copies) > MaximumLabelsPerRequest)
            throw new DomainException(ErrorKind.Validation, $"At most {MaximumLabelsPerRequest} labels can be printed at once.");
        if (request.Items.Select(item => item.VariantId).Distinct().Count() != request.Items.Count)
            throw new DomainException(ErrorKind.Validation, "A size is listed twice.");

        var labels = new List<BarcodeLabelResponse>();
        foreach (var item in request.Items)
        {
            var data = await productListingRepository.FindLabelDataAsync(item.VariantId, request.StoreId, cancellationToken)
                       ?? throw new DomainException(ErrorKind.NotFound, "A chosen size was not found.");
            labels.Add(new BarcodeLabelResponse(data.VariantId, data.Barcode, data.ProductCode, data.ProductName, data.ProductNameUrdu, data.SizeCode,
                                                data.ColorName, data.BrandName, data.SalePrice, item.Copies));
        }
        return labels;
    }

    private async Task<BarcodeReplacementPreviewResponse> PreviewAsync(IReadOnlyList<BarcodeReplacementPairRequest> pairs, IReadOnlyList<int>? rowNumbers,
                                                                       CancellationToken cancellationToken)
    {
        if (pairs is null || pairs.Count == 0)
            throw new DomainException(ErrorKind.Validation, "There are no barcodes to replace.");
        if (pairs.Count > MaximumReplacementRows)
            throw new DomainException(ErrorKind.Validation, $"At most {MaximumReplacementRows} barcodes can be replaced at once.");

        var normalized = pairs.Select(pair => (Old: VariantRules.NormalizeBarcode(pair.OldBarcode), New: VariantRules.NormalizeBarcode(pair.NewBarcode))).ToList();
        var oldBarcodes = normalized.Where(pair => pair.Old.Length > 0).Select(pair => pair.Old).Distinct(StringComparer.Ordinal).ToList();
        var newBarcodes = normalized.Where(pair => pair.New.Length > 0).Select(pair => pair.New).Distinct(StringComparer.Ordinal).ToList();
        var owners = (await barcodeRepository.FindOwnersAsync(oldBarcodes, cancellationToken)).ToDictionary(owner => owner.Barcode, StringComparer.Ordinal);
        var inUse = (await barcodeRepository.FindBarcodesInUseAsync(newBarcodes, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var oldSet = oldBarcodes.ToHashSet(StringComparer.Ordinal);
        var oldCounts = normalized.GroupBy(pair => pair.Old, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var newCounts = normalized.GroupBy(pair => pair.New, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var rows = new List<BarcodeReplacementRowResponse>();
        for (var index = 0; index < normalized.Count; index++)
        {
            var (oldBarcode, newBarcode) = normalized[index];
            owners.TryGetValue(oldBarcode, out var owner);
            var problem = FindProblem(oldBarcode, newBarcode, owner, inUse, oldSet, oldCounts, newCounts);
            rows.Add(new BarcodeReplacementRowResponse(rowNumbers?[index] ?? index + 1, oldBarcode, newBarcode, owner?.ProductName, owner?.SizeCode, problem is null, problem));
        }
        var validCount = rows.Count(row => row.IsValid);
        return new BarcodeReplacementPreviewResponse(rows, validCount, rows.Count - validCount);
    }

    private static string? FindProblem(string oldBarcode, string newBarcode, VariantBarcodeOwner? owner, HashSet<string> inUse, HashSet<string> oldSet,
                                       Dictionary<string, int> oldCounts, Dictionary<string, int> newCounts)
    {
        if (oldBarcode.Length == 0 || newBarcode.Length == 0) return "Both the old and the new barcode are required.";
        try
        {
            VariantRules.EnsureValidBarcode(oldBarcode);
            VariantRules.EnsureValidBarcode(newBarcode);
        }
        catch (DomainException exception)
        {
            return exception.Message;
        }
        if (oldBarcode == newBarcode) return "The new barcode is the same as the old one.";
        if (owner is null) return $"No product has the barcode {oldBarcode}.";
        if (oldCounts[oldBarcode] > 1) return $"The old barcode {oldBarcode} appears more than once in the list.";
        if (newCounts[newBarcode] > 1) return $"The new barcode {newBarcode} appears more than once in the list.";
        // A barcode that another row of the same list gives up is free once that row is applied.
        if (inUse.Contains(newBarcode) && !oldSet.Contains(newBarcode)) return $"The barcode {newBarcode} is already used by another product.";
        return null;
    }
}
