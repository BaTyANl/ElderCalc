using System.IO;
using System.Text.Json.Nodes;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Storage;

/// <summary>
/// Exports and imports a table to a file the user picks. The extension decides the format:
/// .xlsx is an Excel workbook, anything else is the same JSON the table is stored in.
/// </summary>
public static class TableFile
{
    /// <summary>
    /// Filter for the save and open dialogs. JSON comes first: it's the table's native format,
    /// while the workbook is for reading in Excel.
    /// </summary>
    public const string DialogFilter =
        "JSON file (*.json)|*.json|Excel workbook (*.xlsx)|*.xlsx";

    /// <summary>Writes the table to a file: an .xlsx path makes a workbook, anything else JSON.</summary>
    public static void Export(TableViewModel table, string path)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (IsExcel(path))
        {
            ExcelFile.Write(table, path);
            return;
        }

        // Exports are opened and edited by hand, so they're indented — unlike the profile
        // file, which is rewritten on every edit.
        using FileStream stream = File.Create(path);
        TableStorage.Write(stream, TableStorage.Snapshot(table), indented: true);
    }

    /// <summary>Reads a table from a file: replacing the rows, or after them with <paramref name="append"/>.</summary>
    public static void Import(TableViewModel table, string path, bool append = false)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (IsExcel(path))
        {
            ExcelFile.Read(table, path, append);
            return;
        }

        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonArray rows)
        {
            throw new InvalidDataException("The file should contain a list of table rows.");
        }

        TableStorage.FromJson(table, rows, append);
    }

    private static bool IsExcel(string path) =>
        Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
}
