using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LimbusCalc.Theming;

namespace LimbusCalc.Converters;

/// <summary>Проценты из ползунка в долю единицы: непрозрачность задаётся ею.</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double percent ? Math.Clamp(percent / 100.0, 0.0, 1.0) : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double share ? share * 100.0 : 100.0;
}

/// <summary>Цвет из строки вида #RRGGBB — так задана палитра.</summary>
public sealed class HexColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        AppSettings.ParseColor(value as string, Colors.Gray);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Color color ? AppSettings.ToHex(color) : string.Empty;
}
