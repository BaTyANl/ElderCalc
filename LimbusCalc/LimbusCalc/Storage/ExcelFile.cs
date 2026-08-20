using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Storage;

/// <summary>
/// Р§С‚РµРЅРёРµ Рё Р·Р°РїРёСЃСЊ С‚Р°Р±Р»РёС†С‹ РІ .xlsx. Р¤РѕСЂРјР°С‚ СЃРѕР±РёСЂР°РµС‚СЃСЏ РІСЂСѓС‡РЅСѓСЋ, Р±РµР· СЃС‚РѕСЂРѕРЅРЅРёС… Р±РёР±Р»РёРѕС‚РµРє:
/// РЅСѓР¶РЅР° СЂРѕРІРЅРѕ РѕРґРЅР° СЃС‚СЂР°РЅРёС†Р° СЃ С€Р°РїРєРѕР№ Рё СЃС‚СЂРѕРєР°РјРё, Р° С‚СЏРЅСѓС‚СЊ СЂР°РґРё СЌС‚РѕРіРѕ РїР°РєРµС‚ РЅР° РґРµСЃСЏС‚РѕРє
/// РјРµРіР°Р±Р°Р№С‚ РІ РµРґРёРЅС‹Р№ exe РЅРµР·Р°С‡РµРј.
/// РЎС‚СЂРѕРєРё РїРёС€СѓС‚СЃСЏ РІСЃС‚СЂРѕРµРЅРЅС‹РјРё (inlineStr), РїРѕСЌС‚РѕРјСѓ РѕС‚РґРµР»СЊРЅР°СЏ С‚Р°Р±Р»РёС†Р° СЃС‚СЂРѕРє РЅРµ РЅСѓР¶РЅР°;
/// РїСЂРё С‡С‚РµРЅРёРё РѕР±С‰Р°СЏ С‚Р°Р±Р»РёС†Р° СЃС‚СЂРѕРє РІСЃС‘ СЂР°РІРЅРѕ РїРѕРґРґРµСЂР¶РёРІР°РµС‚СЃСЏ вЂ” РµС‘ РєР»Р°РґСѓС‚ Excel Рё Google Sheets.
/// </summary>
public static class ExcelFile
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace PackageRelations =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// РћСЃРЅРѕРІР° С‚РёРїРѕРІ СЃРІСЏР·РµР№. Р”РµСЂР¶РёРј РµС‘ СЃС‚СЂРѕРєРѕР№: Р·РЅР°С‡РµРЅРёРµ Р°С‚СЂРёР±СѓС‚Р° Type вЂ” СЌС‚Рѕ Р°РґСЂРµСЃ С†РµР»РёРєРѕРј,
    /// Р° РЅРµ РёРјСЏ РІ РїСЂРѕСЃС‚СЂР°РЅСЃС‚РІРµ РёРјС‘РЅ, Рё СЃРєР»РµР№РєР° С‡РµСЂРµР· XNamespace РґР°Р»Р° Р±С‹ В«{...}officeDocumentВ».
    /// </summary>
    private const string RelationBase =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace DocumentRelations = RelationBase;

    private static readonly XNamespace ContentTypes =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>Р’С‹РіСЂСѓР¶Р°РµС‚ С‚Р°Р±Р»РёС†Сѓ: РїРµСЂРІР°СЏ СЃС‚СЂРѕРєР° вЂ” РЅР°Р·РІР°РЅРёСЏ СЃС‚РѕР»Р±С†РѕРІ, РґР°Р»СЊС€Рµ РґР°РЅРЅС‹Рµ.</summary>
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
    /// Р§РёС‚Р°РµС‚ С‚Р°Р±Р»РёС†Сѓ РёР· РєРЅРёРіРё. РљР»РµС‚РєРё СЂР°СЃРєР»Р°РґС‹РІР°СЋС‚СЃСЏ РїРѕ РЅР°Р·РІР°РЅРёСЏРј СЃС‚РѕР»Р±С†РѕРІ РёР· РїРµСЂРІРѕР№
    /// СЃС‚СЂРѕРєРё, РїРѕСЌС‚РѕРјСѓ РїРѕСЂСЏРґРѕРє СЃС‚РѕР»Р±С†РѕРІ РІ С„Р°Р№Р»Рµ Р·РЅР°С‡РµРЅРёСЏ РЅРµ РёРјРµРµС‚, Р° Р»РёС€РЅРёРµ РїСЂРѕРїСѓСЃРєР°СЋС‚СЃСЏ.
    /// </summary>
    public static void Read(TableViewModel table, string path)
    {
        ArgumentNullException.ThrowIfNull(table);

        using ZipArchive zip = ZipFile.OpenRead(path);

        ZipArchiveEntry sheet = zip.Entries.FirstOrDefault(entry =>
            entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Р’ РєРЅРёРіРµ РЅРµС‚ РЅРё РѕРґРЅРѕР№ СЃС‚СЂР°РЅРёС†С‹.");

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

        // Пакетом: пересчитывать фильтр и средние после каждой клетки незачем.
        using IDisposable bulk = table.BeginBulkChange();

        table.Clear();

        foreach (string[] values in rows.Skip(1))
        {
            // РџСѓСЃС‚С‹Рµ СЃС‚СЂРѕРєРё РёР· С…РІРѕСЃС‚Р° Р»РёСЃС‚Р° РІ С‚Р°Р±Р»РёС†Сѓ РЅРµ РїРµСЂРµРЅРѕСЃРёРј.
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
        cell.Column.Kind == TableCellKind.Integer ? cell.Number : cell.Value;

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

    /// <summary>РќРѕР»СЊ вЂ” СЌС‚Рѕ СЃС‚РѕР»Р±РµС† A, 26 вЂ” AA.</summary>
    private static string ColumnName(int index)
    {
        string name = string.Empty;

        for (int i = index; i >= 0; i = (i / 26) - 1)
        {
            name = (char)('A' + (i % 26)) + name;
        }

        return name;
    }

    /// <summary>РќРѕРјРµСЂ СЃС‚РѕР»Р±С†Р° РёР· СЃСЃС‹Р»РєРё РІРёРґР° "B12"; -1, РµСЃР»Рё СЃСЃС‹Р»РєРё РЅРµС‚.</summary>
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

    /// <summary>РќР°Р·РІР°РЅРёРµ СЃС‚СЂР°РЅРёС†С‹: Excel РЅРµ РїСѓСЃРєР°РµС‚ С‡Р°СЃС‚СЊ Р·РЅР°РєРѕРІ Рё РґР»РёРЅСѓ Р±РѕР»СЊС€Рµ 31.</summary>
    private static string SheetName(string title)
    {
        string cleaned = new([.. title.Where(symbol => !"\\/?*[]:".Contains(symbol))]);

        return cleaned.Length == 0 ? "Sheet1"
            : cleaned.Length > 31 ? cleaned[..31]
            : cleaned;
    }
}

