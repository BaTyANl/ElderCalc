using System.Windows;
using System.Windows.Media;
using LimbusCalc.Theming;

namespace LimbusCalc.ViewModels;

/// <summary>
/// One outline setting: on or off, color and opacity. Edited in the settings window and
/// repainted through a brush in the application resources.
/// </summary>
public sealed class OutlineSettingsViewModel : ObservableObject, IColorSetting
{
    private readonly string _resourceKey;
    private readonly Action _changed;
    private bool _enabled;
    private Color _color;
    private double _opacity;

    public OutlineSettingsViewModel(
        string title,
        string resourceKey,
        OutlineSettings stored,
        Action changed)
    {
        ArgumentNullException.ThrowIfNull(stored);

        Title = title;
        _resourceKey = resourceKey;
        _changed = changed;
        _enabled = stored.Enabled;
        _color = stored.Color;
        _opacity = stored.Opacity;
    }

    public string Title { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                Apply();
            }
        }
    }

    public Color Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value))
            {
                OnPropertyChanged(nameof(Hex));
                Apply();
            }
        }
    }

    /// <summary>The color as text, so it can be typed in.</summary>
    public string Hex
    {
        get => AppSettings.ToHex(Color);
        set => Color = AppSettings.ParseColor(value, Color);
    }

    /// <summary>Opacity in percent: easier to read on a slider.</summary>
    public double OpacityPercent
    {
        get => Math.Round(_opacity * 100.0);
        set
        {
            double clamped = Math.Clamp(value, 0.0, 100.0) / 100.0;

            if (SetProperty(ref _opacity, clamped, nameof(OpacityPercent)))
            {
                Apply();
            }
        }
    }

    public OutlineSettings ToModel() => new()
    {
        Enabled = Enabled,
        Color = Color,
        Opacity = _opacity,
    };

    /// <summary>
    /// Puts the brush into the application resources. Cells take it through DynamicResource,
    /// so the table repaints immediately and survives a theme switch.
    /// </summary>
    public void Apply()
    {
        SolidColorBrush brush = new(Color) { Opacity = Enabled ? _opacity : 0.0 };
        brush.Freeze();

        Application.Current.Resources[_resourceKey] = brush;
        _changed();
    }
}

/// <summary>A color setting a palette swatch can be applied to.</summary>
public interface IColorSetting
{
    string Hex { get; set; }
}

/// <summary>
/// One end of the damage color scale: a color and its opacity. Any change rebuilds the
/// gradient, so the table repaints right away.
/// </summary>
public sealed class ScaleStopViewModel : ObservableObject, IColorSetting
{
    private readonly Action _changed;
    private Color _color;
    private double _opacity;

    public ScaleStopViewModel(string title, ScaleStop stored, Action changed)
    {
        ArgumentNullException.ThrowIfNull(stored);

        Title = title;
        _color = stored.Color;
        _opacity = stored.Opacity;
        _changed = changed;
    }

    public string Title { get; }

    public Color Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value))
            {
                OnPropertyChanged(nameof(Hex));
                OnPropertyChanged(nameof(PreviewColor));
                _changed();
            }
        }
    }

    /// <summary>The color as text, so it can be typed in.</summary>
    public string Hex
    {
        get => AppSettings.ToHex(Color);
        set => Color = AppSettings.ParseColor(value, Color);
    }

    /// <summary>Opacity in percent: easier to read on a slider.</summary>
    public double OpacityPercent
    {
        get => Math.Round(_opacity * 100.0);
        set
        {
            if (SetProperty(ref _opacity, Math.Clamp(value, 0.0, 100.0) / 100.0, nameof(OpacityPercent)))
            {
                OnPropertyChanged(nameof(PreviewColor));
                _changed();
            }
        }
    }

    /// <summary>The color with its opacity, for the preview strip.</summary>
    public Color PreviewColor => Color.FromArgb((byte)Math.Round(_opacity * 255), Color.R, Color.G, Color.B);

    public ScaleStop ToModel() => new() { Color = Color, Opacity = _opacity };

    internal void Set(ScaleStop stop)
    {
        Color = stop.Color;
        OpacityPercent = stop.Opacity * 100.0;
    }
}

/// <summary>The settings window: theme, table cell look and outlines.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private bool _isDark;
    private bool _showSkillIcons;
    private bool _showDamageScale;

    public SettingsViewModel()
    {
        _isDark = ThemeManager.Current == AppTheme.Dark;
        _showSkillIcons = AppSettings.LoadShowSkillIcons();
        _showDamageScale = AppSettings.LoadShowDamageScale();

        Manual = new OutlineSettingsViewModel(
            "Manual entry",
            ManualOutlineKey,
            AppSettings.LoadOutline("ManualOutline", AppSettings.DefaultManualOutline()),
            Save);

        Calculator = new OutlineSettingsViewModel(
            "From calculator",
            CalculatorOutlineKey,
            AppSettings.LoadOutline("CalculatorOutline", AppSettings.DefaultCalculatorOutline()),
            Save);

        ScaleLow = new ScaleStopViewModel(
            "Lowest damage",
            AppSettings.LoadScaleStop("DamageScaleLow", AppSettings.DefaultScaleLow()),
            OnScaleChanged);

        ScaleHigh = new ScaleStopViewModel(
            "Highest damage",
            AppSettings.LoadScaleStop("DamageScaleHigh", AppSettings.DefaultScaleHigh()),
            OnScaleChanged);
    }

    /// <summary>The damage color scale's ends; cells in between get a mix of the two.</summary>
    public ScaleStopViewModel ScaleLow { get; }

    public ScaleStopViewModel ScaleHigh { get; }

    /// <summary>The gradient table cells draw the damage scale with.</summary>
    public const string DamageScaleGradientKey = "DamageScaleGradient";

    /// <summary>Puts the default green scale back.</summary>
    public void ResetDamageScale()
    {
        ScaleLow.Set(AppSettings.DefaultScaleLow());
        ScaleHigh.Set(AppSettings.DefaultScaleHigh());
    }

    private void OnScaleChanged()
    {
        // Called while the stops are being built too, before both exist.
        if (ScaleLow is null || ScaleHigh is null)
        {
            return;
        }

        ApplyDamageScale();
        Save();
    }

    public const string ManualOutlineKey = "ManualOutlineBrush";

    public const string CalculatorOutlineKey = "CalculatorOutlineBrush";

    /// <summary>Whether skill type and sin icons are shown in table cells.</summary>
    public const string SkillIconVisibilityKey = "SkillIconVisibility";

    /// <summary>Whether the damage color scale is shown in tables.</summary>
    public const string DamageScaleVisibilityKey = "DamageScaleVisibility";

    /// <summary>Damage padding in a cell: the right side reserves room for the icons.</summary>
    public const string DamagePaddingKey = "CellDamagePadding";

    /// <summary>Damage alignment: without icons there's no reason to keep it on the left.</summary>
    public const string DamageAlignmentKey = "CellDamageAlignment";

    public OutlineSettingsViewModel Manual { get; }

    public OutlineSettingsViewModel Calculator { get; }

    /// <summary>Ready-made colors, so a typical choice doesn't need typing a hex value.</summary>
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#DE5040", "#E58B2A", "#E5C22A", "#5BA85B",
        "#3E9BC7", "#7A6BD0", "#C765B0", "#98A0AD",
        "#000000", "#FFFFFF",
    ];

    public bool IsDark
    {
        get => _isDark;
        set
        {
            if (SetProperty(ref _isDark, value))
            {
                ThemeManager.Apply(value ? AppTheme.Dark : AppTheme.Light);
                Save();
            }
        }
    }

    /// <summary>
    /// Skill type and sin icons in table cells. Without them the damage has no reason to hug
    /// the left edge, so it is centered.
    /// </summary>
    public bool ShowSkillIcons
    {
        get => _showSkillIcons;
        set
        {
            if (SetProperty(ref _showSkillIcons, value))
            {
                ApplySkillIcons();
                Save();
            }
        }
    }

    /// <summary>
    /// Damage color scale: the higher the damage in its column, the stronger the cell fill.
    /// Strong entries stand out without sorting.
    /// </summary>
    public bool ShowDamageScale
    {
        get => _showDamageScale;
        set
        {
            if (SetProperty(ref _showDamageScale, value))
            {
                ApplyDamageScale();
                Save();
            }
        }
    }

    /// <summary>Puts the settings into the application resources; called at startup.</summary>
    public void Apply()
    {
        Manual.Apply();
        Calculator.Apply();
        ApplySkillIcons();
        ApplyDamageScale();
    }

    private void ApplyDamageScale()
    {
        ResourceDictionary resources = Application.Current.Resources;

        resources[DamageScaleVisibilityKey] = _showDamageScale ? Visibility.Visible : Visibility.Collapsed;
        resources[DamageScaleGradientKey] = new HeatGradient(ScaleLow.ToModel(), ScaleHigh.ToModel());
    }

    /// <summary>
    /// Puts the cell look into the application resources. Cells take it through DynamicResource,
    /// so the table updates immediately without rebuilding rows.
    /// </summary>
    private void ApplySkillIcons()
    {
        ResourceDictionary resources = Application.Current.Resources;

        resources[SkillIconVisibilityKey] = _showSkillIcons ? Visibility.Visible : Visibility.Collapsed;
        resources[DamagePaddingKey] = _showSkillIcons ? new Thickness(8, 4, 40, 4) : new Thickness(8, 4, 8, 4);
        resources[DamageAlignmentKey] = _showSkillIcons ? TextAlignment.Left : TextAlignment.Center;
    }

    private void Save() =>
        AppSettings.Save(
            IsDark ? AppTheme.Dark : AppTheme.Light,
            Manual.ToModel(),
            Calculator.ToModel(),
            ShowSkillIcons,
            ShowDamageScale,
            ScaleLow.ToModel(),
            ScaleHigh.ToModel());
}
