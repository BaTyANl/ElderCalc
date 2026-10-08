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
/// Switches the theme by replacing the application's first resource dictionary.
/// Styles take their brushes through DynamicResource, so windows repaint immediately.
/// </summary>
public static class ThemeManager
{
    /// <summary>Position of the theme dictionary in App.xaml; it must come first.</summary>
    private const int ThemeDictionaryIndex = 0;

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE: dark window title bar on Windows 10/11.</summary>
    private const int UseImmersiveDarkModeAttribute = 20;

    /// <summary>Must match the theme referenced in App.xaml.</summary>
    public static AppTheme Current { get; private set; } = AppSettings.DefaultTheme;

    /// <summary>Switches the theme and recolors the title bars of open windows.</summary>
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
    /// The title bar is drawn by the system, not WPF, so its theme is set separately.
    /// Call only once the window has a handle.
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
