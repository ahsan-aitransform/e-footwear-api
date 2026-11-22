using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using EFootwear.Domain.Common;

namespace EFootwear.BLL.Common.Spreadsheet;

/// <summary>
/// Reads the cells of an uploaded Excel (.xlsx) or text (.csv) file into rows of text, using only the .NET libraries (no new package). It
/// reads the first worksheet, turns numbers into plain text (a barcode stored as a number stays 8901234567890, not 8.9E+12) and refuses
/// oversized files. Only the cell values are read: formulas show their last stored result, formatting is ignored.
/// </summary>
public static class SpreadsheetReader
{
    public const int MaximumFileBytes = 5 * 1024 * 1024;
    public const long MaximumUncompressedBytes = 20L * 1024 * 1024;
    public const int MaximumRows = 20_000;
    public const int MaximumColumns = 50;

    private static readonly XNamespace MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyList<IReadOnlyList<string>> ReadRows(Stream content, string fileName)
    {
        var bytes = ReadAllBytes(content);
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".xlsx" => ReadWorkbook(bytes),
            ".csv" or ".txt" => ReadDelimitedText(bytes),
            _ => throw new DomainException(ErrorKind.Validation, "Only .xlsx (Excel) and .csv files are accepted."),
        };
    }

    private static byte[] ReadAllBytes(Stream content)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = content.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaximumFileBytes)
                throw new DomainException(ErrorKind.Validation, $"The file can be at most {MaximumFileBytes / (1024 * 1024)} MB.", ErrorCodes.FileTooLarge);
        }
        if (buffer.Length == 0)
            throw new DomainException(ErrorKind.Validation, "The file is empty.");
        return buffer.ToArray();
    }

    // ---------- .csv ----------

    private static IReadOnlyList<IReadOnlyList<string>> ReadDelimitedText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var firstLineEnd = text.IndexOfAny(['\r', '\n']);
        var firstLine = firstLineEnd < 0 ? text : text[..firstLineEnd];
        var delimiter = new[] { ',', ';', '\t' }.MaxBy(candidate => firstLine.Count(character => character == candidate));
        if (firstLine.IndexOf(delimiter) < 0) delimiter = ',';

        var rows = new List<IReadOnlyList<string>>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var insideQuotes = false;

        void EndCell() { cells.Add(cell.ToString().Trim()); cell.Clear(); }
        void EndRow()
        {
            EndCell();
            if (cells.Any(value => value.Length > 0))
            {
                if (rows.Count >= MaximumRows)
                    throw new DomainException(ErrorKind.Validation, $"The file can have at most {MaximumRows} rows.");
                rows.Add(cells.Take(MaximumColumns).ToList());
            }
            cells = [];
        }

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (insideQuotes)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"') { cell.Append('"'); index++; }
                else if (character == '"') insideQuotes = false;
                else cell.Append(character);
            }
            else if (character == '"') insideQuotes = true;
            else if (character == delimiter) EndCell();
            else if (character == '\r') { if (index + 1 < text.Length && text[index + 1] == '\n') index++; EndRow(); }
            else if (character == '\n') EndRow();
            else cell.Append(character);
        }
        if (cell.Length > 0 || cells.Count > 0) EndRow();
        return rows;
    }

    // ---------- .xlsx ----------

    private static IReadOnlyList<IReadOnlyList<string>> ReadWorkbook(byte[] bytes)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (archive.Entries.Count > 2000 || archive.Entries.Sum(entry => entry.Length) > MaximumUncompressedBytes)
                throw new DomainException(ErrorKind.Validation, "The Excel file is too large.", ErrorCodes.FileTooLarge);

            var workbook = LoadXml(archive, "xl/workbook.xml") ?? throw NotAWorkbook();
            var firstSheet = workbook.Descendants(MainNamespace + "sheet").FirstOrDefault() ?? throw NotAWorkbook();
            var relationshipId = (string?)firstSheet.Attribute(RelationshipsNamespace + "id") ?? throw NotAWorkbook();
            var relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels") ?? throw NotAWorkbook();
            var target = relationships.Descendants(PackageRelationshipsNamespace + "Relationship")
                .Where(relationship => (string?)relationship.Attribute("Id") == relationshipId)
                .Select(relationship => (string?)relationship.Attribute("Target")).FirstOrDefault() ?? throw NotAWorkbook();
            var sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            var sheet = LoadXml(archive, sheetPath) ?? throw NotAWorkbook();

            var sharedStrings = LoadXml(archive, "xl/sharedStrings.xml")?.Descendants(MainNamespace + "si")
                .Select(item => string.Concat(item.Descendants(MainNamespace + "t").Select(text => text.Value))).ToList() ?? [];

            var rows = new List<IReadOnlyList<string>>();
            foreach (var rowElement in sheet.Descendants(MainNamespace + "row"))
            {
                var cells = new SortedDictionary<int, string>();
                foreach (var cellElement in rowElement.Elements(MainNamespace + "c"))
                {
                    var column = ColumnIndex((string?)cellElement.Attribute("r"));
                    if (column < 0 || column >= MaximumColumns) continue;
                    cells[column] = CellText(cellElement, sharedStrings);
                }
                if (cells.Count == 0 || cells.Values.All(value => value.Length == 0)) continue;
                if (rows.Count >= MaximumRows)
                    throw new DomainException(ErrorKind.Validation, $"The file can have at most {MaximumRows} rows.");
                var width = cells.Keys.Max() + 1;
                rows.Add(Enumerable.Range(0, width).Select(index => cells.GetValueOrDefault(index, "")).ToList());
            }
            return rows;
        }
        catch (InvalidDataException)
        {
            throw NotAWorkbook();
        }
    }

    private static string CellText(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (type == "inlineStr")
            return string.Concat(cell.Descendants(MainNamespace + "t").Select(text => text.Value)).Trim();

        var value = cell.Element(MainNamespace + "v")?.Value;
        if (string.IsNullOrEmpty(value)) return "";
        switch (type)
        {
            case "s":
                return int.TryParse(value, CultureInfo.InvariantCulture, out var stringIndex) && stringIndex >= 0 && stringIndex < sharedStrings.Count
                    ? sharedStrings[stringIndex].Trim() : "";
            case "b":
                return value == "1" ? "TRUE" : "FALSE";
            case "str":
            case "e":
                return value.Trim();
            default:
                return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString("0.##########", CultureInfo.InvariantCulture)
                    : value.Trim();
        }
    }

    private static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference)) return -1;
        var index = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsAsciiLetter(character)) break;
            index = index * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
        }
        return index - 1;
    }

    private static XDocument? LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static DomainException NotAWorkbook() =>
        new(ErrorKind.Validation, "The file is not a readable Excel (.xlsx) workbook.", ErrorCodes.UnsupportedFileType);
}
