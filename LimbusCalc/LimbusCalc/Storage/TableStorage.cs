using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LimbusCalc.Calculation;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Storage;

/// <summary>Чем закончилось чтение таблицы из профиля.</summary>
public enum TableLoadOutcome
{
    /// <summary>Файл прочитан.</summary>
    Loaded,

    /// <summary>Файла нет — таблица начинается пустой.</summary>
    Missing,

    /// <summary>Файл испорчен, взята прошлая версия из резервной копии.</summary>
    RestoredFromBackup,

    /// <summary>Не прочитался ни файл, ни копия — таблица пустая.</summary>
    Failed,
}

/// <summary>Одна клетка, как она прочитана из файла, — ещё без привязки к таблице.</summary>
public sealed record TableCellData(string Value, ElementOption? Type, ElementOption? Sin, string? Setup);

/// <summary>
/// Содержимое таблицы, прочитанное из файла: строки как пары «ключ столбца — клетка».
/// Собирается в фоновом потоке, а в таблицу ставится уже в потоке окна.
/// </summary>
public sealed class TableData(IReadOnlyList<IReadOnlyList<KeyValuePair<string, TableCellData>>> rows)
{
    public static TableData Empty { get; } = new([]);

    public IReadOnlyList<IReadOnlyList<KeyValuePair<string, TableCellData>>> Rows { get; } = rows;
}

/// <summary>
/// Итог чтения. <see cref="CanSave"/> ложно, когда испорченный файл не удалось
/// даже скопировать в сторону: писать поверх него значило бы потерять его насовсем.
/// </summary>
public sealed record TableLoadResult(
    TableLoadOutcome Outcome,
    TableData Data,
    string? Problem,
    string? BrokenCopy,
    bool CanSave);

/// <summary>
/// Копия содержимого таблицы на момент сохранения. Снимается в потоке окна за
/// доли миллисекунды: строки клеток неизменяемы, копировать приходится только ссылки.
/// Пишется потом в фоне, пока в таблице продолжают работать.
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
/// Хранит справочные таблицы между запусками. Каждая таблица лежит в своём файле
/// в профиле пользователя: программу могут положить в папку без права записи,
/// а отдельные файлы проще передавать и подменять по одному.
/// Содержимое файла — список строк, где каждая строка представлена объектом
/// с ключами столбцов: так файл переживает добавление и перестановку столбцов.
/// Пустые и счётные клетки не пишутся — первые при чтении и так пусты, вторые
/// пересчитываются.
/// </summary>
public static class TableStorage
{
    public const string IdFileName = "idTable.json";

    public const string EgoFileName = "egoTable.json";

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ElderCalc");

    /// <summary>
    /// Кириллица и скобки в названиях остаются как есть, а не превращаются в \uXXXX:
    /// файл читают и глазами. Для HTML такое не годится, но это не HTML.
    /// </summary>
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>Полный путь к файлу таблицы — его показываем пользователю.</summary>
    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>Прошлая версия файла; её оставляет каждое сохранение.</summary>
    public static string BackupOf(string path) => path + ".bak";

    // ---------------------------------------------------------------- чтение

    /// <summary>
    /// Читает таблицу из профиля. Можно звать из любого потока: к таблице на экране
    /// не прикасается. Испорченный файл откладывается в сторону под своим именем,
    /// и вместо него берётся резервная копия, если она читается.
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
                    // Копия тоже не читается — остаёмся с пустой таблицей ниже.
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

    /// <summary>Разбирает список строк в данные таблицы. Годится для фонового потока.</summary>
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
    /// Ставит прочитанное в таблицу вместо её содержимого. Только в потоке окна.
    /// Ключи, которых у таблицы нет, пропускаются: столбец могли убрать.
    /// </summary>
    public static void Apply(TableViewModel table, TableData data)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(data);

        // Пакетом: фильтр, средние и счётные клетки считаются один раз в конце.
        using IDisposable bulk = table.BeginBulkChange();

        table.Clear();

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

    /// <summary>Заменяет содержимое таблицы прочитанным списком строк.</summary>
    public static void FromJson(TableViewModel table, JsonArray rows) => Apply(table, Parse(rows));

    private static TableData ParseFile(string path)
    {
        using FileStream stream = File.OpenRead(path);

        return JsonNode.Parse(stream) is JsonArray rows
            ? Parse(rows)
            : throw new InvalidDataException("В файле ожидался список строк таблицы.");
    }

    /// <summary>
    /// Обычная клетка лежит одним значением — числом или строкой. Клетка урона
    /// с типом, грехом или набором — объектом, где отсутствующее поле значит «нет».
    /// </summary>
    private static TableCellData ParseCell(JsonNode value)
    {
        if (value is not JsonObject skill)
        {
            return new TableCellData(ReadText(value), null, null, null);
        }

        // Наборы, записанные раньше целиком, ужимаем прямо тут: следующее
        // сохранение запишет уже короткими.
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
    /// Кладёт испорченный файл рядом под именем с датой, чтобы следующее сохранение
    /// его не затёрло. Не вышло — пусто: тогда в этот файл писать нельзя.
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
    /// Значение клетки текстом. В файле оно могло оказаться и числом, и строкой —
    /// например, после выгрузки из таблицы или правки руками. Узел спрашиваем через
    /// TryGetValue: разобранный из файла и собранный в памяти устроены по-разному,
    /// и приведение к JsonElement на втором просто падает.
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

    // ---------------------------------------------------------------- запись

    /// <summary>Снимок таблицы для записи. Только в потоке окна; дальше — где угодно.</summary>
    public static TableSnapshot Snapshot(TableViewModel table)
    {
        ArgumentNullException.ThrowIfNull(table);

        List<List<SnapshotCell>> rows = new(table.Rows.Count);

        foreach (TableRowViewModel row in table.Rows)
        {
            List<SnapshotCell> cells = [];

            foreach (TableCell cell in row.Cells)
            {
                // Счётное пересчитается при чтении, пустое и так будет пустым.
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
    /// Пишет таблицу в профиль так, чтобы сбой посреди записи ничего не испортил:
    /// сначала во временный файл, потом подмена, а прежняя версия остаётся копией.
    /// Можно звать из фонового потока. Ошибку не глотает — о ней должны узнать.
    /// </summary>
    public static void Save(string fileName, TableSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Directory.CreateDirectory(Folder);

        string path = PathOf(fileName);
        string temporary = path + ".tmp";

        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            // Без отступов: файл читает программа, а лишние пробелы его раздувают.
            Write(stream, snapshot, indented: false);
            stream.Flush(flushToDisk: true);
        }

        ReplaceWithBackup(temporary, path, BackupOf(path));
    }

    /// <summary>
    /// Пишет снимок в поток как JSON. Наборы в сжатом виде вставляются как есть,
    /// без разбора; для выгрузки с отступами их приходится разобрать, чтобы
    /// отступы легли и внутри.
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

    /// <summary>Содержимое таблицы в том же виде, в каком оно ложится в файл.</summary>
    public static JsonArray ToJson(TableViewModel table)
    {
        using MemoryStream stream = new();
        Write(stream, Snapshot(table), indented: false);

        return JsonNode.Parse(stream.ToArray()) as JsonArray ?? [];
    }

    /// <summary>
    /// Простая клетка — одно значение. Клетка урона с типом, грехом или набором —
    /// объект, в котором пишется только то, что есть.
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

    /// <summary>Число пишется числом; если в клетке вдруг не число — строкой, чтобы не потерять.</summary>
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
    /// Ставит новый файл на место старого одним действием системы, а старый оставляет
    /// копией. Где подмена не поддерживается, делаем то же двумя шагами.
    /// </summary>
    private static void ReplaceWithBackup(string temporary, string path, string backup)
    {
        if (!File.Exists(path))
        {
            File.Move(temporary, path);
            return;
        }

        try
        {
            File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            File.Copy(path, backup, overwrite: true);
            File.Move(temporary, path, overwrite: true);
        }
    }
}
