using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LimbusCalc.Calculation;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Storage;

/// <summary>How reading a table from the profile ended.</summary>
public enum TableLoadOutcome
{
    /// <summary>The file was read.</summary>
    Loaded,

    /// <summary>No file: the table starts empty.</summary>
    Missing,

    /// <summary>The file was broken; the previous version was taken from the backup.</summary>
    RestoredFromBackup,

    /// <summary>Neither the file nor the backup could be read: the table is empty.</summary>
    Failed,
}

/// <summary>One cell as read from the file, not yet attached to a table.</summary>
public sealed record TableCellData(string Value, ElementOption? Type, ElementOption? Sin, string? Setup);

/// <summary>
/// Table content read from a file: rows as "column key — cell" pairs.
/// Built on a background thread and put into the table on the UI thread.
/// </summary>
public sealed class TableData(IReadOnlyList<IReadOnlyList<KeyValuePair<string, TableCellData>>> rows)
{
    public static TableData Empty { get; } = new([]);

    public IReadOnlyList<IReadOnlyList<KeyValuePair<string, TableCellData>>> Rows { get; } = rows;
}

/// <summary>
/// The result of reading. <see cref="CanSave"/> is false when the broken file couldn't even
/// be copied aside: writing over it would lose it for good.
/// </summary>
public sealed record TableLoadResult(
    TableLoadOutcome Outcome,
    TableData Data,
    string? Problem,
    string? BrokenCopy,
    bool CanSave);

/// <summary>
/// A copy of the table's content at save time. Taken on the UI thread in a fraction of a
/// millisecond: cell strings are immutable, so only references are copied. It is written
/// out in the background while the user keeps working.
/// </summary>
public sealed class TableSnapshot
{
    internal TableSnapshot(List<List<SnapshotCell>> rows) => Rows = rows;

    internal List<List<SnapshotCell>> Rows { get; }
}

internal readonly record struct SnapshotCell(
    string Key,
    bool IsNumber,
    string Value,
    string? Type,
    string? Sin,
    string? Setup);

/// <summary>
/// Stores the reference tables between launches. Each table has its own file in the user
/// profile: the exe may sit in a read-only folder, and separate files are easier to share
/// and replace one at a time.
/// The file is a list of rows, each an object keyed by column, so it survives columns
/// being added or reordered. Empty and computed cells aren't written: the former are
/// empty on reading anyway and the latter are recalculated.
/// </summary>
public static class TableStorage
{
    public const string IdFileName = "idTable.json";

    public const string EgoFileName = "egoTable.json";

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ElderCalc");

    /// <summary>
    /// Cyrillic and brackets in names stay as they are instead of becoming \uXXXX:
    /// people read this file too. That's unsafe for HTML, but this isn't HTML.
    /// </summary>
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>Full path of a table file; this is what the user is shown.</summary>
    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>The previous version of the file; every save leaves one.</summary>
    public static string BackupOf(string path) => path + ".bak";

    // ---------------------------------------------------------------- reading

    /// <summary>
    /// Reads a table from the profile. Safe to call from any thread: it doesn't touch the
    /// table on screen. A broken file is set aside under its own name and the backup is
    /// used instead, if it can be read.
    /// </summary>
    public static TableLoadResult Read(string fileName)
    {
        string path = PathOf(fileName);

        if (!File.Exists(path))
        {
            return new TableLoadResult(TableLoadOutcome.Missing, TableData.Empty, null, null, CanSave: true);
        }

        try
        {
            return new TableLoadResult(TableLoadOutcome.Loaded, ParseFile(path), null, null, CanSave: true);
        }
        catch (Exception error)
        {
            string? broken = KeepBrokenCopy(path);
            string backup = BackupOf(path);

            if (File.Exists(backup))
            {
                try
                {
                    return new TableLoadResult(
                        TableLoadOutcome.RestoredFromBackup,
                        ParseFile(backup),
                        error.Message,
                        broken,
                        CanSave: true);
                }
                catch (Exception)
                {
                    // The backup is unreadable too — fall through to an empty table.
                }
            }

            return new TableLoadResult(
                TableLoadOutcome.Failed,
                TableData.Empty,
                error.Message,
                broken,
                CanSave: broken is not null);
        }
    }

    /// <summary>Parses a list of rows into table data. Safe on a background thread.</summary>
    public static TableData Parse(JsonArray rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        List<IReadOnlyList<KeyValuePair<string, TableCellData>>> parsed = new(rows.Count);

        foreach (JsonNode? node in rows)
        {
            if (node is not JsonObject stored)
            {
                continue;
            }

            List<KeyValuePair<string, TableCellData>> row = new(stored.Count);

            foreach ((string key, JsonNode? value) in stored)
            {
                if (value is not null)
                {
                    row.Add(new KeyValuePair<string, TableCellData>(key, ParseCell(value)));
                }
            }

            parsed.Add(row);
        }

        return new TableData(parsed);
    }

    /// <summary>
    /// Puts the read data into the table in place of its content. UI thread only.
    /// Keys the table doesn't have are skipped: the column may have been removed. With
    /// <paramref name="append"/> the rows go after the existing ones instead.
    /// </summary>
    public static void Apply(TableViewModel table, TableData data, bool append = false)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(data);

        // In bulk: filters, averages and computed cells are recalculated once at the end.
        using IDisposable bulk = table.BeginBulkChange();

        if (!append)
        {
            table.Clear();
        }

        foreach (IReadOnlyList<KeyValuePair<string, TableCellData>> stored in data.Rows)
        {
            TableRowViewModel row = table.AddRow();

            foreach ((string key, TableCellData value) in stored)
            {
                if (row.CellOf(key) is not TableCell cell)
                {
                    continue;
                }

                cell.Value = value.Value;
                cell.SkillType = value.Type;
                cell.SkillSin = value.Sin;
                cell.Setup = value.Setup;
            }
        }
    }

    /// <summary>Replaces the table's content with a parsed list of rows.</summary>
    public static void FromJson(TableViewModel table, JsonArray rows, bool append = false) =>
        Apply(table, Parse(rows), append);

    private static TableData ParseFile(string path)
    {
        using FileStream stream = File.OpenRead(path);

        return JsonNode.Parse(stream) is JsonArray rows
            ? Parse(rows)
            : throw new InvalidDataException("The file should contain a list of table rows.");
    }

    /// <summary>
    /// A plain cell is stored as a single value, a number or a string. A damage cell with a
    /// type, sin or setup is an object, where a missing field means "none".
    /// </summary>
    private static TableCellData ParseCell(JsonNode value)
    {
        if (value is not JsonObject skill)
        {
            return new TableCellData(ReadText(value), null, null, null);
        }

        // Setups written in full by older versions are compacted right here, so the next
        // save writes them short.
        string? setup = skill["setup"] is JsonObject stored
            ? SetupDefaults.Compact(stored).ToJsonString()
            : null;

        return new TableCellData(
            skill["damage"] is JsonNode damage ? ReadText(damage) : string.Empty,
            ReadElement(skill["type"]),
            ReadElement(skill["sin"]),
            setup);
    }

    /// <summary>
    /// Moves a broken file aside under a dated name so the next save doesn't overwrite it.
    /// Returns null on failure — then this file must not be written to.
    /// </summary>
    private static string? KeepBrokenCopy(string path)
    {
        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string copy = Path.Combine(
                Path.GetDirectoryName(path) ?? Folder,
                $"{Path.GetFileNameWithoutExtension(path)}.broken-{stamp}{Path.GetExtension(path)}");

            File.Copy(path, copy, overwrite: true);
            return copy;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A cell's value as text. In the file it may be a number or a string — for example after
    /// an export or a manual edit. The node is queried with TryGetValue because nodes parsed
    /// from a file and nodes built in memory differ, and casting the latter to JsonElement throws.
    /// </summary>
    private static string ReadText(JsonNode value)
    {
        if (value is JsonValue json)
        {
            if (json.TryGetValue(out string? text))
            {
                return text ?? string.Empty;
            }

            if (json.TryGetValue(out double number))
            {
                return number.ToString(CultureInfo.InvariantCulture);
            }
        }

        return value.ToJsonString();
    }

    private static ElementOption? ReadElement(JsonNode? value) =>
        value is JsonValue json
            && json.TryGetValue(out string? name)
            && Enum.TryParse(name, out Element parsed)
                ? ElementOptions.For(parsed)
                : null;

    // ---------------------------------------------------------------- writing

    /// <summary>A snapshot of the table for writing. Take it on the UI thread; use it anywhere.</summary>
    public static TableSnapshot Snapshot(TableViewModel table)
    {
        ArgumentNullException.ThrowIfNull(table);

        List<List<SnapshotCell>> rows = new(table.Rows.Count);

        foreach (TableRowViewModel row in table.Rows)
        {
            List<SnapshotCell> cells = [];

            foreach (TableCell cell in row.Cells)
            {
                // Computed cells are recalculated on reading; empty ones stay empty anyway.
                if (cell.Column.Kind == TableCellKind.Computed
                    || (cell.IsEmpty && cell.SkillType is null && cell.SkillSin is null && !cell.HasSetup))
                {
                    continue;
                }

                cells.Add(new SnapshotCell(
                    cell.Column.Key,
                    cell.Column.Kind == TableCellKind.Integer,
                    cell.Value,
                    cell.SkillType?.Element.ToString(),
                    cell.SkillSin?.Element.ToString(),
                    cell.HasSetup ? cell.Setup : null));
            }

            rows.Add(cells);
        }

        return new TableSnapshot(rows);
    }

    /// <summary>
    /// Writes a table to the profile so a crash mid-write can't corrupt anything: first to a
    /// temporary file, then a swap, and the previous version stays as a backup.
    /// Safe on a background thread. Doesn't swallow errors — the caller must hear about them.
    /// </summary>
    public static void Save(string fileName, TableSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Directory.CreateDirectory(Folder);

        string path = PathOf(fileName);
        string temporary = path + ".tmp";

        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            // No indentation: a program reads this file, and whitespace only bloats it.
            Write(stream, snapshot, indented: false);
            stream.Flush(flushToDisk: true);
        }

        ReplaceWithBackup(temporary, path, BackupOf(path));
    }

    /// <summary>
    /// Writes a snapshot to a stream as JSON. Compact setups are inserted as they are,
    /// without parsing; for an indented export they have to be parsed so the indentation
    /// applies inside them too.
    /// </summary>
    public static void Write(Stream stream, TableSnapshot snapshot, bool indented)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(snapshot);

        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions
        {
            Indented = indented,
            Encoder = Encoder,
        });

        writer.WriteStartArray();

        foreach (List<SnapshotCell> row in snapshot.Rows)
        {
            writer.WriteStartObject();

            foreach (SnapshotCell cell in row)
            {
                writer.WritePropertyName(cell.Key);
                WriteCell(writer, cell, indented);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>The table's content in the same shape it takes in the file.</summary>
    public static JsonArray ToJson(TableViewModel table)
    {
        using MemoryStream stream = new();
        Write(stream, Snapshot(table), indented: false);

        return JsonNode.Parse(stream.ToArray()) as JsonArray ?? [];
    }

    /// <summary>
    /// A plain cell is a single value. A damage cell with a type, sin or setup is an object
    /// that holds only what is present.
    /// </summary>
    private static void WriteCell(Utf8JsonWriter writer, SnapshotCell cell, bool indented)
    {
        if (!cell.IsNumber)
        {
            writer.WriteStringValue(cell.Value);
            return;
        }

        bool plain = cell.Type is null && cell.Sin is null && cell.Setup is null;

        if (plain)
        {
            WriteNumber(writer, cell.Value);
            return;
        }

        writer.WriteStartObject();

        if (cell.Value.Length > 0)
        {
            writer.WritePropertyName("damage");
            WriteNumber(writer, cell.Value);
        }

        if (cell.Type is not null)
        {
            writer.WriteString("type", cell.Type);
        }

        if (cell.Sin is not null)
        {
            writer.WriteString("sin", cell.Sin);
        }

        if (cell.Setup is not null)
        {
            writer.WritePropertyName("setup");

            if (indented)
            {
                JsonNode.Parse(cell.Setup)?.WriteTo(writer);
            }
            else
            {
                writer.WriteRawValue(cell.Setup);
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>Numbers are written as numbers; anything else as a string so nothing is lost.</summary>
    private static void WriteNumber(Utf8JsonWriter writer, string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }

    /// <summary>
    /// Puts the new file in place of the old one in a single system call and keeps the old
    /// one as a backup. Where the swap isn't supported, does the same in two steps.
    /// </summary>
    private static void ReplaceWithBackup(string temporary, string path, string backup)
    {
        if (!File.Exists(path))
        {
            File.Move(temporary, path);
            return;
        }

        // The backup is ours, and a read-only flag on it is inherited from the main file,
        // not anyone's decision. Left in place, no save would ever succeed again, even
        // once the file itself is writable.
        AllowWriting(backup);

        try
        {
            File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            File.Copy(path, backup, overwrite: true);
            AllowWriting(backup);
            File.Move(temporary, path, overwrite: true);
        }
    }

    private static void AllowWriting(string file)
    {
        FileInfo info = new(file);

        if (info.Exists && info.IsReadOnly)
        {
            info.IsReadOnly = false;
        }
    }
}
