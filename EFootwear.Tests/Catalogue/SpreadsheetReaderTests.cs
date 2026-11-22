using System.IO.Compression;
using System.Text;
using EFootwear.BLL.Common.Spreadsheet;
using EFootwear.Domain.Common;

namespace EFootwear.Tests.Catalogue;

/// <summary>The upload reader used by Replace Barcodes: .csv with different delimiters and quotes, .xlsx with shared strings, inline strings and numeric cells.</summary>
public class SpreadsheetReaderTests
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static MemoryStream Text(string content) => new(Encoding.UTF8.GetBytes(content));

    private static byte[] Workbook(string sheetRowsXml, string? sharedStringsXml = null)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
            Add("xl/workbook.xml", $"""<workbook xmlns="{MainNamespace}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            Add("xl/worksheets/sheet1.xml", $"""<worksheet xmlns="{MainNamespace}"><sheetData>{sheetRowsXml}</sheetData></worksheet>""");
            if (sharedStringsXml is not null) Add("xl/sharedStrings.xml", $"""<sst xmlns="{MainNamespace}">{sharedStringsXml}</sst>""");
        }
        return stream.ToArray();
    }

    [Fact]
    public void A_csv_with_commas_is_read_into_rows_and_empty_lines_are_skipped()
    {
        var rows = SpreadsheetReader.ReadRows(Text("Old,New\r\nA1,B1\r\n\r\nA2,B2\r\n"), "pairs.csv");
        Assert.Equal(3, rows.Count);
        Assert.Equal(["A2", "B2"], rows[2]);
    }

    [Fact]
    public void A_csv_with_semicolons_and_quoted_cells_is_read_correctly()
    {
        var rows = SpreadsheetReader.ReadRows(Text("\"a;b\";\"say \"\"hi\"\"\"\n1;2\n"), "pairs.csv");
        Assert.Equal(["a;b", "say \"hi\""], rows[0]);
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public void A_workbook_is_read_with_shared_strings_inline_strings_and_numbers_as_plain_text()
    {
        var bytes = Workbook(
            """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2"><v>8901234567890</v></c><c r="B2" t="inlineStr"><is><t>NEW-1</t></is></c></row>
            <row r="3"><c r="A3"><v>8.9012345678900E+12</v></c><c r="C3"><v>7</v></c></row>
            """,
            "<si><t>Old Barcode</t></si><si><t>New Barcode</t></si>");

        var rows = SpreadsheetReader.ReadRows(new MemoryStream(bytes), "pairs.xlsx");

        Assert.Equal(["Old Barcode", "New Barcode"], rows[0]);
        Assert.Equal(["8901234567890", "NEW-1"], rows[1]);
        Assert.Equal(["8901234567890", "", "7"], rows[2]);
    }

    [Fact]
    public void A_file_of_another_type_is_refused()
    {
        var error = Assert.Throws<DomainException>(() => SpreadsheetReader.ReadRows(Text("x"), "pairs.pdf"));
        Assert.Equal(ErrorKind.Validation, error.Kind);
    }

    [Fact]
    public void Bytes_that_are_not_a_workbook_are_refused_with_a_clear_message()
    {
        var error = Assert.Throws<DomainException>(() => SpreadsheetReader.ReadRows(Text("this is not a zip file"), "pairs.xlsx"));
        Assert.Equal(ErrorCodes.UnsupportedFileType, error.ErrorCode);
    }

    [Fact]
    public void An_empty_file_is_refused() =>
        Assert.Throws<DomainException>(() => SpreadsheetReader.ReadRows(new MemoryStream(), "pairs.csv"));
}
