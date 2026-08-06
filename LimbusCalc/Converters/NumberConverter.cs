using System.Globalization;
using System.Windows.Data;

namespace LimbusCalc.Converters;

/// <summary>
/// Число в текстовом поле. Показывает с точкой, а на вводе принимает и точку, и запятую,
/// чтобы английский интерфейс не мешал набирать с русской раскладкой.
/// ConverterParameter задаёт формат вывода (например F2).
/// </summary>
public sealed class NumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double number = value switch
        {
            double d => d,
            int i => i,
            _ => double.NaN,
        };

        if (double.IsNaN(number))
        {
            return string.Empty;
        }

        string format = parameter as string ?? "0.####";
        return number.ToString(format, CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool wantsInteger = targetType == typeof(int);
        string text = (value as string ?? string.Empty).Trim().Replace(',', '.');

        if (text.Length == 0)
        {
            return wantsInteger ? 0 : 0.0;
        }

        // Пока строка недонабрана ("-", "1."), оставляем прежнее значение вместо ошибки.
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
        {
            return Binding.DoNothing;
        }

        return wantsInteger
            ? (int)Math.Round(result, MidpointRounding.AwayFromZero)
            : result;
    }
}
