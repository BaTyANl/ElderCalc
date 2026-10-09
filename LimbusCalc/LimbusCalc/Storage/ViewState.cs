using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Storage;

/// <summary>The window: where it was, how big, and whether it was maximized.</summary>
public sealed class WindowViewState
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Maximized { get; set; }
}

/// <summary>One remembered sort key.</summary>
public sealed class SortState
{
    public string Key { get; set; } = string.Empty;

    public bool Descending { get; set; }
}

/// <summary>A remembered value range: bounds exactly as typed.</summary>
public sealed class RangeState
{
    public string Min { get; set; } = string.Empty;

    public string Max { get; set; } = string.Empty;
}

/// <summary>
/// The look of one table: sorting, filters and column layout.
/// </summary>
public sealed class TableViewState
{
    /// <summary>The single sort key of older versions; read only when <see cref="Sort"/> is empty.</summary>
    public string? SortKey { get; set; }

    public bool SortDescending { get; set; }

    /// <summary>Sort keys from the main one to the last.</summary>
    public List<SortState> Sort { get; set; } = [];

    public List<SkillSortKey> SortPriority { get; set; } = [];

    /// <summary>Checked filter items, keyed by the filter list's title.</summary>
    public Dictionary<string, List<string>> Filters { get; set; } = [];

    public string Search { get; set; } = string.Empty;

    public bool FavoritesOnly { get; set; }

    /// <summary>Value ranges by column key; only set ones are stored.</summary>
    public Dictionary<string, RangeState> Ranges { get; set; } = [];

    /// <summary>Keys of hidden columns.</summary>
    public List<string> Hidden { get; set; } = [];

    /// <summary>Column keys in screen order; empty means the default order.</summary>
    public List<string> ColumnOrder { get; set; } = [];

    /// <summary>Widths set by dragging, by column key.</summary>
    public Dictionary<string, double> ColumnWidths { get; set; } = [];

    /// <summary>Takes the current look of a table to remember it.</summary>
    public static TableViewState Capture(TableViewModel table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return new TableViewState
        {
            SortKey = table.SortKey,
            SortDescending = table.SortDescending,
            Sort = [.. table.SortKeys.Select(key => new SortState { Key = key.Key, Descending = key.Descending })],
            SortPriority = [.. table.SortPriority.Select(option => option.Key)],
            Filters = table.Filter.Selections.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
            Search = table.Filter.Search,
            FavoritesOnly = table.Filter.FavoritesOnly,
            Ranges = table.Filter.Ranges
                .Where(range => range.IsSet)
                .ToDictionary(range => range.Key, range => new RangeState { Min = range.Min, Max = range.Max }),
            Hidden = [.. table.Columns.Where(column => column.IsHidden).Select(column => column.Key)],
            ColumnOrder = table.HasCustomLayout ? [.. table.DisplayColumns.Select(column => column.Key)] : [],
            ColumnWidths = table.Columns
                .Where(column => column.UserWidth is not null)
                .ToDictionary(column => column.Key, column => column.UserWidth!.Value),
        };
    }

    /// <summary>
    /// Restores the remembered look. Applied after the rows are read, since there's nothing
    /// to sort before that. Anything the table no longer has — a column or a filter item — is skipped.
    /// </summary>
    public void ApplyTo(TableViewModel table)
    {
        ArgumentNullException.ThrowIfNull(table);

        foreach (TableColumn column in table.Columns)
        {
            table.SetColumnHidden(column, Hidden.Contains(column.Key));
        }

        table.RestoreLayout(ColumnOrder, ColumnWidths);

        table.Filter.Restore(
            Filters.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value),
            Search);

        table.Filter.FavoritesOnly = FavoritesOnly;

        foreach (RangeFilterViewModel range in table.Filter.Ranges)
        {
            RangeState? stored = Ranges.GetValueOrDefault(range.Key);
            range.Min = stored?.Min ?? string.Empty;
            range.Max = stored?.Max ?? string.Empty;
        }

        // Older files remember a single key.
        List<(string Key, bool Descending)> sort = Sort.Count > 0
            ? [.. Sort.Select(key => (key.Key, key.Descending))]
            : SortKey is null ? [] : [(SortKey, SortDescending)];

        table.RestoreSort(sort, SortPriority);
    }
}

/// <summary>
/// The app's look between launches: window, open tab and table views. Kept apart from
/// settings, because the settings window rewrites its file as a whole and would wipe this.
/// Losing this file is harmless — the app just opens as on first launch.
/// </summary>
public sealed class ViewState
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ElderCalc",
        "view.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public WindowViewState? Window { get; set; }

    public int? Tab { get; set; }

    /// <summary>Table views keyed by table title: "ID", "E.G.O.".</summary>
    public Dictionary<string, TableViewState> Tables { get; set; } = [];

    public static ViewState Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<ViewState>(File.ReadAllText(FilePath), Options) ?? new ViewState()
                : new ViewState();
        }
        catch (Exception)
        {
            // A broken view file must not prevent startup.
            return new ViewState();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception)
        {
            // The view wasn't saved; next launch simply opens with the default view.
        }
    }
}
