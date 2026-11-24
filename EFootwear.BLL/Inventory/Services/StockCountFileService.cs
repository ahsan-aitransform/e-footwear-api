using System.Globalization;
using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common.Spreadsheet;
using EFootwear.BLL.Inventory.Abstractions;
using EFootwear.BLL.Inventory.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Inventory;

namespace EFootwear.BLL.Inventory.Services;

/// <summary>
/// Reads a count file (.xlsx or .csv) for the non-zero stock list: the first row names the columns (a barcode column and a count column), every other row is one
/// size. Nothing is saved: the answer lists the rows that match a size and the rows that cannot be used, so the person checks them before posting the count update.
/// </summary>
public sealed class StockCountFileService(IInventoryReadRepository readRepository, ICurrentUser currentUser)
{
    private static readonly string[] BarcodeHeaders = ["barcode", "bar code", "bar_code", "barcode no", "code"];
    private static readonly string[] CountHeaders = ["count", "counted", "counted stock", "stock", "quantity", "qty"];

    public async Task<StockCountFilePreviewResponse> PreviewAsync(Guid storeId, Stream content, string fileName, CancellationToken cancellationToken)
    {
        if (storeId == Guid.Empty || !currentUser.CanAccessStore(storeId))
            throw new DomainException(ErrorKind.Forbidden, "You do not have access to this store.");

        var fileRows = SpreadsheetReader.ReadRows(content, fileName);
        if (fileRows.Count < 2)
            throw new DomainException(ErrorKind.Validation, "The file needs a heading row and at least one row of data.");
        if (fileRows.Count - 1 > InventoryRules.MaximumFileRows)
            throw new DomainException(ErrorKind.Validation, $"The file can have at most {InventoryRules.MaximumFileRows} rows.");

        var headings = fileRows[0].Select(heading => heading.Trim().ToLowerInvariant()).ToList();
        var barcodeColumn = headings.FindIndex(heading => BarcodeHeaders.Contains(heading));
        var countColumn = headings.FindIndex(heading => CountHeaders.Contains(heading));
        if (barcodeColumn < 0 || countColumn < 0)
            throw new DomainException(ErrorKind.Validation, "The first row must have a Barcode column and a Count column.");

        var problems = new List<StockCountFileProblemResponse>();
        var wanted = new List<(int RowNumber, string Barcode, decimal Count)>();
        var seenBarcodes = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < fileRows.Count; index++)
        {
            var rowNumber = index + 1;
            var cells = fileRows[index];
            var barcode = barcodeColumn < cells.Count ? cells[barcodeColumn].Trim() : "";
            var countText = countColumn < cells.Count ? cells[countColumn].Trim() : "";
            if (barcode.Length == 0 && countText.Length == 0) continue;
            if (barcode.Length == 0) { problems.Add(new(rowNumber, barcode, "The barcode is empty.")); continue; }
            if (!decimal.TryParse(countText, NumberStyles.Number, CultureInfo.InvariantCulture, out var count)
                || count < 0 || count > InventoryRules.MaximumQuantityPerLine || decimal.Round(count, InventoryRules.QuantityDecimalPlaces) != count)
            { problems.Add(new(rowNumber, barcode, "The count must be zero or more, with at most 3 decimals.")); continue; }
            if (!seenBarcodes.Add(barcode)) { problems.Add(new(rowNumber, barcode, "This barcode appears more than once in the file.")); continue; }
            wanted.Add((rowNumber, barcode, count));
        }

        var items = (await readRepository.FindItemsByBarcodesAsync(storeId, wanted.Select(row => row.Barcode).ToList(), cancellationToken))
            .ToDictionary(item => item.Barcode, StringComparer.Ordinal);
        var rows = new List<StockCountFileRowResponse>();
        foreach (var (rowNumber, barcode, count) in wanted)
        {
            if (!items.TryGetValue(barcode, out var item))
            {
                problems.Add(new(rowNumber, barcode, "This barcode is not a known article size."));
                continue;
            }
            rows.Add(new StockCountFileRowResponse(item.VariantId, item.Barcode, item.ProductName, item.SizeCode, item.StockQuantity, count));
        }
        return new StockCountFilePreviewResponse(rows, problems.OrderBy(problem => problem.RowNumber).ToList());
    }
}
