using System.IO.Compression;
using System.Security;
using System.Text;

namespace EFootwear.BLL.Common.Spreadsheet;

/// <summary>Writes a one-sheet Excel (.xlsx) workbook of text cells, using only the .NET libraries (the counterpart of <see cref="SpreadsheetReader"/>).</summary>
public static class SpreadsheetWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] WriteWorkbook(string sheetName, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        const string Declaration = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""";
        var sheet = new StringBuilder(Declaration)
            .Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetFormatPr defaultRowHeight="15" defaultColWidth="20"/><sheetData>""");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            sheet.Append($"<row r=\"{rowIndex + 1}\">");
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Count; columnIndex++)
                sheet.Append($"<c r=\"{ColumnName(columnIndex)}{rowIndex + 1}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(rows[rowIndex][columnIndex])}</t></is></c>");
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", Declaration + """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            Add(archive, "_rels/.rels", Declaration + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add(archive, "xl/workbook.xml", Declaration + $"""<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{SecurityElement.Escape(sheetName)}" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add(archive, "xl/_rels/workbook.xml.rels", Declaration + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            Add(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return buffer.ToArray();
    }

    private static void Add(ZipArchive archive, string path, string xml)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(xml);
    }

    private static string ColumnName(int index) => (index < 26 ? "" : ColumnName(index / 26 - 1)) + (char)('A' + index % 26);
}
