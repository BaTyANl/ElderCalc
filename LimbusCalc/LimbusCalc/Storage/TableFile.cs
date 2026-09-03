using System.IO;
using System.Text.Json.Nodes;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Storage;

/// <summary>
/// Выгрузка и загрузка таблицы файлом по выбору пользователя. Формат определяется
/// расширением: .xlsx — книга Excel, всё остальное — тот же JSON, в котором таблица
/// хранится между запусками.
/// </summary>
public static class TableFile
{
    /// <summary>
    /// Фильтр для диалогов сохранения и открытия. JSON стоит первым: он и есть
    /// родной формат таблицы, а книга Excel нужна, когда её открывают глазами.
    /// </summary>
    public const string DialogFilter =
        "JSON file (*.json)|*.json|Excel workbook (*.xlsx)|*.xlsx";

    public static void Export(TableViewModel table, string path)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (IsExcel(path))
        {
            ExcelFile.Write(table, path);
            return;
        }

        // Выгрузку открывают и правят руками, поэтому с отступами — в отличие
        // от файла в профиле, который переписывается на каждой правке.
        File.WriteAllText(path, TableStorage.ToJson(table).ToJsonString(JsonFormat.Readable));
    }

    public static void Import(TableViewModel table, string path)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (IsExcel(path))
        {
            ExcelFile.Read(table, path);
            return;
        }

        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonArray rows)
        {
            throw new InvalidDataException("В файле ожидался список строк таблицы.");
        }

        TableStorage.FromJson(table, rows);
    }

    private static bool IsExcel(string path) =>
        Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
}
