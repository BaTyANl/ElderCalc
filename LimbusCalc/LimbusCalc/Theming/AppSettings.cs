using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;

namespace LimbusCalc.Theming;

/// <summary>A table cell outline: whether it's shown, its color and its opacity.</summary>
public sealed class OutlineSettings
{
    public required bool Enabled { get; set; }

    public required Color Color { get; set; }

    /// <summary>Opacity from 0 to 1.</summary>
    public required double Opacity { get; set; }
}

/// <summary>One end of the damage color scale: a color and how opaque it is.</summary>
public sealed class ScaleStop
{
    public required Color Color { get; set; }

    /// <summary>Opacity from 0 to 1.</summary>
    public required double Opacity { get; set; }
}

/// <summary>
/// App settings between launches: theme, cell look and outlines. The file lives in the user
/// profile rather than next to the exe, which may sit in a read-only folder.
/// </summary>
public static class AppSettings
{
    /// <summary>What the app opens with until the user picks something.</summary>
    public const AppTheme DefaultTheme = AppTheme.Dark;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ElderCalc",
        "settings.json");

    /// <summary>
    /// Outline of manually filled cells: muted gray. Off by default; outlines are turned on
    /// in settings when you need to see where the damage came from.
    /// </summary>
    public static OutlineSettings DefaultManualOutline() => new()
    {
        Enabled = false,
        Color = Color.FromRgb(0x98, 0xA0, 0xAD),
        Opacity = 1.0,
    };

    /// <summary>Outline of cells from the calculator: accent red; also off at first.</summary>
    public static OutlineSettings DefaultCalculatorOutline() => new()
    {
        Enabled = false,
        Color = Color.FromRgb(0xDE, 0x50, 0x40),
        Opacity = 1.0,
    };

    /// <summary>Skill type and sin icons in table cells are shown unless turned off.</summary>
    public const bool DefaultShowSkillIcons = true;

    /// <summary>The damage color scale starts off: it's a view for comparing, not for entering data.</summary>
    public const bool DefaultShowDamageScale = false;

    public static bool LoadShowDamageScale()
    {
        JsonObject? stored = Read();

        return stored?["ShowDamageScale"] is JsonNode node
            ? Flag(node, DefaultShowDamageScale)
            : DefaultShowDamageScale;
    }

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

    /// <summary>
    /// The lowest value of the damage scale: the same green as the highest, nearly transparent,
    /// so weak values are barely tinted.
    /// </summary>
    public static ScaleStop DefaultScaleLow() => new() { Color = Color.FromRgb(0x3F, 0xB2, 0x7F), Opacity = 0.05 };

    /// <summary>The highest value of the damage scale: a clear green.</summary>
    public static ScaleStop DefaultScaleHigh() => new() { Color = Color.FromRgb(0x3F, 0xB2, 0x7F), Opacity = 0.55 };

    public static ScaleStop LoadScaleStop(string key, ScaleStop fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        if (Read()?[key] is not JsonObject stored)
        {
            return fallback;
        }

        return new ScaleStop
        {
            Color = ParseColor((string?)stored["Color"], fallback.Color),
            Opacity = stored["Opacity"] is JsonNode opacity
                ? Math.Clamp(Number(opacity, fallback.Opacity), 0.0, 1.0)
                : fallback.Opacity,
        };
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

    /// <summary>Writes all settings at once: the file is tiny, no need to merge it piece by piece.</summary>
    public static void Save(
        AppTheme theme,
        OutlineSettings manual,
        OutlineSettings calculator,
        bool showSkillIcons,
        bool showDamageScale,
        ScaleStop scaleLow,
        ScaleStop scaleHigh)
    {
        ArgumentNullException.ThrowIfNull(manual);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(scaleLow);
        ArgumentNullException.ThrowIfNull(scaleHigh);

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
                ["ShowDamageScale"] = showDamageScale,
                ["DamageScaleLow"] = Write(scaleLow),
                ["DamageScaleHigh"] = Write(scaleHigh),
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
            // Settings aren't critical: if saving fails, the next launch uses the defaults.
        }
    }

    private static JsonObject Write(ScaleStop stop) => new()
    {
        ["Color"] = ToHex(stop.Color),
        ["Opacity"] = Math.Round(stop.Opacity, 3),
    };

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
            // A broken or inaccessible file must not prevent startup.
            return null;
        }
    }

    public static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Parses a #RRGGBB color; returns the fallback on any error.</summary>
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
