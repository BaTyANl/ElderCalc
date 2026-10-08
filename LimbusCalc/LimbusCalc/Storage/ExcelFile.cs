using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Storage;

/// <summary>
/// Reads and writes a table as .xlsx. The format is assembled by hand, without third-party
/// libraries: exactly one sheet with a header and rows is needed, and bundling a package of
/// a dozen megabytes into the single exe for that isn't worth it.
/// Strings are written inline (inlineStr), so no shared string table is needed; reading
/// still supports the shared string table, since Excel and Google Sheets produce one.
/// </summary>
public static class ExcelFile
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace PackageRelations =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// The base of relationship types. Kept as a string: the Type attribute holds the whole
    /// address rather than a namespaced name, and XNamespace would produce "{...}officeDocument".
    /// </summary>
    private const string RelationBase =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace DocumentRelations = RelationBase;

    private static readonly XNamespace ContentTypes =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>Exports the table: the first row holds column titles, then the data.</summary>
    public static void Write(TableViewModel table, string path)
    {
        ArgumentNullException.ThrowIfNull(table);

        XElement sheetData = new(Main + "sheetData");
        int number = 1;

        sheetData.Add(BuildRow(number++, [.. table.Columns.Select(column => (object?)column.Title)]));

        foreach (TableRowViewModel row in table.Rows)
        {
            sheetData.Add(BuildRow(number++, [.. row.Cells.Select(ValueOf)]));
        }

        XDocument sheet = new(new XElement(Main + "worksheet", sheetData));

        using FileStream file = File.Create(path);
        using ZipArchive zip = new(file, ZipArchiveMode.Create);

        Put(zip, "[Content_Types].xml", new XDocument(
            new XElement(ContentTypes + "Types",
                new XElement(ContentTypes + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ContentTypes + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ContentTypes + "Override",
                    new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ContentTypes + "Override",
                    new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                    new XAttribute("ContentType",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));

        Put(zip, "_rels/.rels", new XDocument(
            new XElement(PackageRelations + "Relationships",
                new XElement(PackageRelations + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", RelationBase + "/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml")))));

        Put(zip, "xl/workbook.xml", new XDocument(
            new XElement(Main + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", DocumentRelations),
                new XElement(Main + "sheets",
                    new XElement(Main + "sheet",
                        new XAttribute("name", SheetName(table.Title)),
                        new XAttribute("sheetId", 1),
                        new XAttribute(DocumentRelations + "id", "rId1"))))));

        Put(zip, "xl/_rels/workbook.xml.rels", new XDocument(
            new XElement(PackageRelations + "Relationships",
                new XElement(PackageRelations + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", RelationBase + "/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml")))));

        Put(zip, "xl/worksheets/sheet1.xml", sheet);
    }

    /// <summary>
    /// Reads a table from a workbook. Cells are matched by the column titles in the first
    /// row, so the column order in the file doesn't matter and unknown columns are skipped.
    /// </summary>
    public static void Read(TableViewModel table, string path, bool append = false)
    {
        ArgumentNullException.ThrowIfNull(table);

        using ZipArchive zip = ZipFile.OpenRead(path);

        ZipArchiveEntry sheet = zip.Entries.FirstOrDefault(entry =>
            entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The workbook has no sheets.");

        string[] shared = ReadSharedStrings(zip);

        using Stream stream = sheet.Open();
        XDocument document = XDocument.Load(stream);

        List<string[]> rows =
        [
            .. document.Descendants(Main + "row").Select(row => ReadRow(row, shared)),
        ];

        if (rows.Count == 0)
        {
            return;
        }

        string[] headers = rows[0];

        // In bulk: there's no point recalculating filters and averages after every cell.
        using IDisposable bulk = table.BeginBulkChange();

        if (!append)
        {
            table.Clear();
        }

        foreach (string[] values in rows.Skip(1))
        {
            // Empty rows at the end of the sheet are not carried into the table.
            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            TableRowViewModel row = table.AddRow();

            for (int i = 0; i < values.Length && i < headers.Length; i++)
            {
                TableCell? cell = row.CellOf(headers[i]);

                if (cell is not null)
                {
                    cell.Value = values[i];
                }
            }
        }
    }

    private static object? ValueOf(TableCell cell) =>
        cell.Column.Kind is TableCellKind.Integer or TableCellKind.Computed ? cell.Number : cell.Value;

    private static XElement BuildRow(int number, IReadOnlyList<object?> values)
    {
        XElement row = new(Main + "row", new XAttribute("r", number));

        for (int i = 0; i < values.Count; i++)
        {
            object? value = values[i];

            if (value is null || (value is string text && text.Length == 0))
            {
                continue;
            }

            string reference = ColumnName(i) + number.ToString(CultureInfo.InvariantCulture);

            row.Add(value is double number2
                ? new XElement(Main + "c",
                    new XAttribute("r", reference),
                    new XElement(Main + "v", number2.ToString(CultureInfo.InvariantCulture)))
                : new XElement(Main + "c",
                    new XAttribute("r", reference),
                    new XAttribute("t", "inlineStr"),
                    new XElement(Main + "is", new XElement(Main + "t", value))));
        }

        return row;
    }

    private static string[] ReadRow(XElement row, string[] shared)
    {
        Dictionary<int, string> byColumn = [];

        foreach (XElement cell in row.Elements(Main + "c"))
        {
            int index = ColumnIndex((string?)cell.Attribute("r") ?? string.Empty);

            if (index >= 0)
            {
                byColumn[index] = ReadCell(cell, shared);
            }
        }

        if (byColumn.Count == 0)
        {
            return [];
        }

        string[] values = new string[byColumn.Keys.Max() + 1];

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = byColumn.TryGetValue(i, out string? value) ? value : string.Empty;
        }

        return values;
    }

    private static string ReadCell(XElement cell, string[] shared)
    {
        string type = (string?)cell.Attribute("t") ?? string.Empty;

        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(Main + "t").Select(t => t.Value));
        }

        string value = cell.Element(Main + "v")?.Value ?? string.Empty;

        if (type != "s")
        {
            return value;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            && index >= 0
            && index < shared.Length
            ? shared[index]
            : string.Empty;
    }

    private static string[] ReadSharedStrings(ZipArchive zip)
    {
        ZipArchiveEntry? entry = zip.GetEntry("xl/sharedStrings.xml");

        if (entry is null)
        {
            return [];
        }

        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);

        return
        [
            .. document.Root?.Elements(Main + "si").Select(item =>
                string.Concat(item.Descendants(Main + "t").Select(t => t.Value))) ?? [],
        ];
    }

    private static void Put(ZipArchive zip, string name, XDocument content)
    {
        using Stream stream = zip.CreateEntry(name).Open();
        using StreamWriter writer = new(stream, new UTF8Encoding(false));

        content.Save(writer);
    }

    /// <summary>Zero is column A, 26 is AA.</summary>
    private static string ColumnName(int index)
    {
        string name = string.Empty;

        for (int i = index; i >= 0; i = (i / 26) - 1)
        {
            name = (char)('A' + (i % 26)) + name;
        }

        return name;
    }

    /// <summary>Column index from a reference like "B12"; -1 if there's none.</summary>
    private static int ColumnIndex(string reference)
    {
        int index = 0;
        int letters = 0;

        foreach (char symbol in reference)
        {
            if (!char.IsAsciiLetter(symbol))
            {
                break;
            }

            index = (index * 26) + (char.ToUpperInvariant(symbol) - 'A' + 1);
            letters++;
        }

        return letters == 0 ? -1 : index - 1;
    }

    /// <summary>Sheet name: Excel rejects some characters and anything longer than 31.</summary>
    private static string SheetName(string title)
    {
        string cleaned = new([.. title.Where(symbol => !"\\/?*[]:".Contains(symbol))]);

        return cleaned.Length == 0 ? "Sheet1"
            : cleaned.Length > 31 ? cleaned[..31]
            : cleaned;
    }
}

