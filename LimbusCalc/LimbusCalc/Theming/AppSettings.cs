using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;

namespace LimbusCalc.Theming;

/// <summary>Обводка клетки таблицы: показывать ли её, каким цветом и насколько густо.</summary>
public sealed class OutlineSettings
{
    public required bool Enabled { get; set; }

    public required Color Color { get; set; }

    /// <summary>Непрозрачность от 0 до 1.</summary>
    public required double Opacity { get; set; }
}

/// <summary>
/// Настройки приложения между запусками: тема и обводка клеток. Файл лежит в профиле
/// пользователя, а не рядом с программой: её могут положить в папку без права записи.
/// </summary>
public static class AppSettings
{
    /// <summary>Чем открывается приложение, пока пользователь ничего не выбрал.</summary>
    public const AppTheme DefaultTheme = AppTheme.Dark;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ElderCalc",
        "settings.json");

    /// <summary>Цвет обводки клеток, заполненных руками, — приглушённый серый.</summary>
    public static OutlineSettings DefaultManualOutline() => new()
    {
        Enabled = true,
        Color = Color.FromRgb(0x98, 0xA0, 0xAD),
        Opacity = 1.0,
    };

    /// <summary>Цвет обводки клеток из калькулятора — акцентный красный.</summary>
    public static OutlineSettings DefaultCalculatorOutline() => new()
    {
        Enabled = true,
        Color = Color.FromRgb(0xDE, 0x50, 0x40),
        Opacity = 1.0,
    };

    /// <summary>Иконки типа и греха в клетках справочника показываем, пока не сказано иное.</summary>
    public const bool DefaultShowSkillIcons = true;

    public static bool LoadShowSkillIcons()
    {
        JsonObject? stored = Read();

        return stored?["ShowSkillIcons"] is JsonNode node
            ? Flag(node, DefaultShowSkillIcons)
            : DefaultShowSkillIcons;
    }

    public static AppTheme LoadTheme()
    {
        JsonObject? stored = Read();

        return stored?["Theme"] is JsonNode node
            && Enum.TryParse((string?)node, out AppTheme theme)
                ? theme
                : DefaultTheme;
    }

    public static OutlineSettings LoadOutline(string key, OutlineSettings fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        if (Read()?[key] is not JsonObject stored)
        {
            return fallback;
        }

        return new OutlineSettings
        {
            Enabled = stored["Enabled"] is JsonNode enabled ? Flag(enabled, fallback.Enabled) : fallback.Enabled,
            Color = ParseColor((string?)stored["Color"], fallback.Color),
            Opacity = stored["Opacity"] is JsonNode opacity
                ? Math.Clamp(Number(opacity, fallback.Opacity), 0.0, 1.0)
                : fallback.Opacity,
        };
    }

    /// <summary>Пишет настройки целиком: файл маленький, собирать его по частям незачем.</summary>
    public static void Save(
        AppTheme theme,
        OutlineSettings manual,
        OutlineSettings calculator,
        bool showSkillIcons)
    {
        ArgumentNullException.ThrowIfNull(manual);
        ArgumentNullException.ThrowIfNull(calculator);

        try
        {
            string? folder = Path.GetDirectoryName(SettingsPath);

            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
            }

            JsonObject root = new()
            {
                ["Theme"] = theme.ToString(),
                ["ShowSkillIcons"] = showSkillIcons,
                ["ManualOutline"] = Write(manual),
                ["CalculatorOutline"] = Write(calculator),
            };

            File.WriteAllText(SettingsPath, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        catch (Exception)
        {
            // Настройки не критичны: не смогли сохранить — в следующий раз откроемся
            // со значениями по умолчанию.
        }
    }

    private static JsonObject Write(OutlineSettings outline) => new()
    {
        ["Enabled"] = outline.Enabled,
        ["Color"] = ToHex(outline.Color),
        ["Opacity"] = Math.Round(outline.Opacity, 3),
    };

    private static JsonObject? Read()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject
                : null;
        }
        catch (Exception)
        {
            // Испорченный или недоступный файл не должен мешать запуску.
            return null;
        }
    }

    public static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Разбирает цвет вида #RRGGBB; при любой ошибке возвращает запасной.</summary>
    public static Color ParseColor(string? text, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        try
        {
            return ColorConverter.ConvertFromString(text.Trim()) is Color parsed ? parsed : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static bool Flag(JsonNode node, bool fallback)
    {
        try
        {
            return node.GetValue<bool>();
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static double Number(JsonNode node, double fallback)
    {
        try
        {
            return node.GetValue<double>();
        }
        catch (Exception)
        {
            return double.TryParse(
                (string?)node,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed)
                    ? parsed
                    : fallback;
        }
    }
}
