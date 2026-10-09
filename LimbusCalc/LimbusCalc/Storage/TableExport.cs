using System.IO;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Storage;

/// <summary>Formats a table can be exported to.</summary>
public enum TableExportFormat
{
    Json,
    Excel,
}

/// <summary>What to export: which rows and columns, and in which format.</summary>
/// <param name="ShownRowsOnly">Only rows the filters show; cells hidden by filters stay blank.</param>
/// <param name="ShownColumnsOnly">Only columns that aren't hidden.</param>
public sealed record TableExportOptions(bool ShownRowsOnly, bool ShownColumnsOnly, TableExportFormat Format);

/// <summary>
/// Exports a table, whole or as it's filtered, to JSON or an Excel workbook. Rows and
/// columns go in screen order.
/// </summary>
public static class TableExport
{
    /// <summary>The save dialog filter for a format.</summary>
    public static string FilterFor(TableExportFormat format) => format switch
    {
        TableExportFormat.Excel => "Excel workbook (*.xlsx)|*.xlsx",
        _ => "JSON file (*.json)|*.json",
    };

    public static string ExtensionFor(TableExportFormat format) => format switch
    {
        TableExportFormat.Excel => ".xlsx",
        _ => ".json",
    };

    /// <summary>Rows to export in screen order: all of them, or only those the filters show.</summary>
    public static IReadOnlyList<TableRowViewModel> RowsOf(TableViewModel table, bool shownOnly)
    {
        ArgumentNullException.ThrowIfNull(table);

        return [.. table.Rows.Where(row => !shownOnly || row.IsVisible)];
    }

    /// <summary>Columns to export in screen order: all of them, or only those not hidden.</summary>
    public static IReadOnlyList<TableColumn> ColumnsOf(TableViewModel table, bool shownOnly)
    {
        ArgumentNullException.ThrowIfNull(table);

        return [.. table.DisplayColumns.Where(column => !shownOnly || !column.IsHidden)];
    }

    /// <summary>Writes the table to a file.</summary>
    public static void Write(TableViewModel table, string path, TableExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<TableRowViewModel> rows = RowsOf(table, options.ShownRowsOnly);
        IReadOnlyList<TableColumn> columns = ColumnsOf(table, options.ShownColumnsOnly);

        // With "shown rows only" a cell hidden by the type or sin filter is left out too:
        // the export matches what's on screen.
        Func<TableCell, bool> include = options.ShownRowsOnly ? cell => cell.IsVisible : _ => true;

        if (options.Format == TableExportFormat.Excel)
        {
            ExcelFile.Write(table.Title, rows, columns, include, path);
            return;
        }

        using FileStream stream = File.Create(path);
        TableStorage.Write(stream, TableStorage.Snapshot(rows, columns, include), indented: true);
    }
}
