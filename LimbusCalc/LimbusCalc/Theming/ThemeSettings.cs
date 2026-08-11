using System.IO;
using System.Text.Json;

namespace LimbusCalc.Theming;

/// <summary>
/// Запоминает выбранную тему между запусками. Файл лежит в профиле пользователя,
/// а не рядом с программой: её могут положить в папку без права записи.
/// </summary>
public static class ThemeSettings
{
    /// <summary>Чем открывается приложение, пока пользователь ничего не выбрал.</summary>
    public const AppTheme Default = AppTheme.Dark;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ElderCalc",
        "settings.json");

    public static AppTheme Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                StoredSettings? stored =
                    JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(SettingsPath));

                if (stored?.Theme is not null && Enum.TryParse(stored.Theme, out AppTheme saved))
                {
                    return saved;
                }
            }
        }
        catch (Exception)
        {
            // Настройки не критичны: испорченный или недоступный файл не должен
            // мешать запуску — просто открываемся с темой по умолчанию.
        }

        return Default;
    }

    public static void Save(AppTheme theme)
    {
        try
        {
            string? folder = Path.GetDirectoryName(SettingsPath);

            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
            }

            StoredSettings stored = new() { Theme = theme.ToString() };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(stored));
        }
        catch (Exception)
        {
            // Не смогли сохранить — не беда, в следующий раз откроется тема по умолчанию.
        }
    }

    private sealed class StoredSettings
    {
        public string? Theme { get; set; }
    }
}
