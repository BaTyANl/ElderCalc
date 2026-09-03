using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LimbusCalc.Calculation;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Storage;

/// <summary>
/// Хранит справочные таблицы между запусками. Каждая таблица лежит в своём файле
/// в профиле пользователя: программу могут положить в папку без права записи,
/// а отдельные файлы проще передавать и подменять по одному.
/// Содержимое файла — список строк, где каждая строка представлена объектом
/// с подписями столбцов: так файл переживает добавление и перестановку столбцов.
/// </summary>
public static class TableStorage
{
    public const string IdFileName = "idTable.json";

    public const string EgoFileName = "egoTable.json";

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ElderCalc");

    /// <summary>Полный путь к файлу таблицы — его показываем пользователю.</summary>
    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>Наполняет таблицу сохранённым содержимым. Нет файла — таблица остаётся пустой.</summary>
    public static void Load(string fileName, TableViewModel table)
    {
        ArgumentNullException.ThrowIfNull(table);

        try
        {
            string path = PathOf(fileName);

            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray rows)
            {
                LoadRows(table, rows);
            }
        }
        catch (Exception)
        {
            // Испорченный или недоступный файл не должен мешать запуску:
            // открываемся с пустой таблицей.
        }
    }

    public static void Save(string fileName, TableViewModel table)
    {
        ArgumentNullException.ThrowIfNull(table);

        try
        {
            Directory.CreateDirectory(Folder);

            // Без отступов: файл читает программа, а лишние пробелы на тысяче строк
            // раздувают его почти вдвое.
            File.WriteAllText(PathOf(fileName), SaveRows(table).ToJsonString());
        }
        catch (Exception)
        {
            // Не смогли сохранить — не роняем приложение из-за справочника.
        }
    }

    /// <summary>Содержимое таблицы в том же виде, в каком оно ложится в файл.</summary>
    public static JsonArray ToJson(TableViewModel table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return SaveRows(table);
    }

    /// <summary>Заменяет содержимое таблицы прочитанным списком строк.</summary>
    public static void FromJson(TableViewModel table, JsonArray rows)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(rows);

        LoadRows(table, rows);
    }

    private static JsonArray SaveRows(TableViewModel table)
    {
        JsonArray rows = [];

        foreach (TableRowViewModel row in table.Rows)
        {
            JsonObject stored = [];

            foreach (TableCell cell in row.Cells)
            {
                stored[cell.Column.Key] = SaveCell(cell);
            }

            rows.Add(stored);
        }

        return rows;
    }

    /// <summary>
    /// Обычная клетка пишется одним значением — числом или строкой. Клетка скилла
    /// с типом или грехом становится объектом: простые случаи остаются простыми.
    /// </summary>
    private static JsonNode? SaveCell(TableCell cell)
    {
        if (cell.Column.Kind is not TableCellKind.Integer and not TableCellKind.Computed)
        {
            return JsonValue.Create(cell.Value);
        }

        JsonNode? damage = cell.Number is double number ? JsonValue.Create(number) : null;

        if (cell.SkillType is null && cell.SkillSin is null && !cell.HasSetup)
        {
            return damage;
        }

        return new JsonObject
        {
            ["damage"] = damage,
            ["type"] = cell.SkillType is null ? null : JsonValue.Create(cell.SkillType.Element.ToString()),
            ["sin"] = cell.SkillSin is null ? null : JsonValue.Create(cell.SkillSin.Element.ToString()),
            // Набор кладём разобранным, а не строкой: иначе в файле окажется
            // JSON внутри строки со всеми экранированными кавычками.
            ["setup"] = cell.Setup is null ? null : JsonNode.Parse(cell.Setup),
        };
    }

    private static void LoadRows(TableViewModel table, JsonArray rows)
    {
        // Загрузка идёт пакетом: фильтр и средние считаются один раз в конце.
        using IDisposable bulk = table.BeginBulkChange();

        table.Clear();

        foreach (JsonNode? node in rows)
        {
            if (node is not JsonObject stored)
            {
                continue;
            }

            TableRowViewModel row = table.AddRow();

            foreach ((string title, JsonNode? value) in stored)
            {
                TableCell? cell = row.CellOf(title);

                if (cell is not null && value is not null)
                {
                    LoadCell(cell, value);
                }
            }
        }
    }

    private static void LoadCell(TableCell cell, JsonNode value)
    {
        if (value is JsonObject skill)
        {
            cell.Value = skill["damage"] is JsonNode damage ? ReadText(damage) : string.Empty;
            cell.SkillType = ReadElement(skill["type"]);
            cell.SkillSin = ReadElement(skill["sin"]);
            cell.Setup = skill["setup"] is JsonObject setup ? setup.ToJsonString() : null;
            return;
        }

        cell.Value = ReadText(value);
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
}
