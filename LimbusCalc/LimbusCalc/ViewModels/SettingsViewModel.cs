using System.Windows;
using System.Windows.Media;
using LimbusCalc.Theming;

namespace LimbusCalc.ViewModels;

/// <summary>
/// Настройка одной обводки: включена ли, каким цветом и насколько густо. Меняется
/// в окне настроек, а перерисовывается через кисть в ресурсах приложения.
/// </summary>
public sealed class OutlineSettingsViewModel : ObservableObject
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

    /// <summary>Цвет строкой — его можно вписать руками.</summary>
    public string Hex
    {
        get => AppSettings.ToHex(Color);
        set => Color = AppSettings.ParseColor(value, Color);
    }

    /// <summary>Непрозрачность в процентах: так понятнее в ползунке.</summary>
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
    /// Кладёт кисть в ресурсы приложения. Клетки берут её через DynamicResource,
    /// поэтому таблица перекрашивается сразу и переживает смену темы.
    /// </summary>
    public void Apply()
    {
        SolidColorBrush brush = new(Color) { Opacity = Enabled ? _opacity : 0.0 };
        brush.Freeze();

        Application.Current.Resources[_resourceKey] = brush;
        _changed();
    }
}

/// <summary>Окно настроек: тема и обводка клеток справочника.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private bool _isDark;
    private bool _showSkillIcons;

    public SettingsViewModel()
    {
        _isDark = ThemeManager.Current == AppTheme.Dark;
        _showSkillIcons = AppSettings.LoadShowSkillIcons();

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
    }

    public const string ManualOutlineKey = "ManualOutlineBrush";

    public const string CalculatorOutlineKey = "CalculatorOutlineBrush";

    /// <summary>Показывать ли иконки типа и греха в клетках справочника.</summary>
    public const string SkillIconVisibilityKey = "SkillIconVisibility";

    /// <summary>Отступы урона в клетке: справа они держат место под иконки.</summary>
    public const string DamagePaddingKey = "CellDamagePadding";

    /// <summary>Куда прижат урон: без иконок ему незачем стоять слева.</summary>
    public const string DamageAlignmentKey = "CellDamageAlignment";

    public OutlineSettingsViewModel Manual { get; }

    public OutlineSettingsViewModel Calculator { get; }

    /// <summary>Готовые цвета: набирать hex руками ради обычного выбора незачем.</summary>
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#DE5040", "#E58B2A", "#E5C22A", "#5BA85B",
        "#3E9BC7", "#7A6BD0", "#C765B0", "#98A0AD",
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
    /// Иконки типа и греха в клетках справочника. Без них урону незачем жаться
    /// к левому краю — он встаёт по центру клетки.
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

    /// <summary>Ставит настройки в ресурсы приложения — вызывается при запуске.</summary>
    public void Apply()
    {
        Manual.Apply();
        Calculator.Apply();
        ApplySkillIcons();
    }

    /// <summary>
    /// Кладёт вид клетки в ресурсы приложения. Клетки берут это через DynamicResource,
    /// поэтому таблица перестраивается сразу, без пересборки строк.
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
            ShowSkillIcons);
}
