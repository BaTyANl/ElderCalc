using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LimbusCalc.Theming;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// Переключает тему, подменяя первый словарь ресурсов приложения.
/// Кисти в стилях берутся через DynamicResource, поэтому окно перекрашивается сразу.
/// </summary>
public static class ThemeManager
{
    /// <summary>Позиция словаря темы в App.xaml — он должен идти первым.</summary>
    private const int ThemeDictionaryIndex = 0;

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE: тёмный заголовок окна в Windows 10/11.</summary>
    private const int UseImmersiveDarkModeAttribute = 20;

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static void Apply(AppTheme theme)
    {
        Current = theme;

        ResourceDictionary themeDictionary = new()
        {
            Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative),
        };

        Application.Current.Resources.MergedDictionaries[ThemeDictionaryIndex] = themeDictionary;

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window, theme);
        }
    }

    /// <summary>
    /// Заголовок окна рисует система, а не WPF, поэтому его тему приходится задавать отдельно.
    /// Вызывать только когда окно уже создано, иначе описателя ещё нет.
    /// </summary>
    public static void ApplyTitleBar(Window window, AppTheme theme)
    {
        nint handle = new WindowInteropHelper(window).Handle;

        if (handle == nint.Zero)
        {
            return;
        }

        int useDarkMode = theme == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkModeAttribute, ref useDarkMode, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int valueSize);
}
