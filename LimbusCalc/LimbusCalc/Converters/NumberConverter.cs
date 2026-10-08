using System.Globalization;
using System.Windows.Data;

namespace LimbusCalc.Converters;

/// <summary>
/// A number in a text box. Displayed with a dot, but input accepts both a dot and a comma,
/// so a Russian keyboard layout doesn't get in the way of the English UI.
/// ConverterParameter sets the display format (for example F2).
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

        // While the text is still incomplete ("-", "1.") keep the old value instead of failing.
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
        {
            return Binding.DoNothing;
        }

        return wantsInteger
            ? (int)Math.Round(result, MidpointRounding.AwayFromZero)
            : result;
    }
}
