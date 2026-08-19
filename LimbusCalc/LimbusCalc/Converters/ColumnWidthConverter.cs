using System.Globalization;
using System.Windows.Data;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Converters;

/// <summary>
/// Ширина клетки таблицы. Обычный столбец отдаёт свою ширину, растяжимый забирает
/// всё, что осталось от окна: сколько занимают остальные, столбец знает заранее.
/// </summary>
public sealed class ColumnWidthConverter : IMultiValueConverter
{
    /// <param name="values">Столбец и ширина видимой части таблицы.</param>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [TableColumn column, double available, ..])
        {
            return double.NaN;
        }

        if (!column.Stretch || double.IsNaN(available) || available <= 0.0)
        {
            return column.Width;
        }

        // Единица — на внешнюю рамку таблицы, иначе строка вылезает за неё
        // и появляется лишняя горизонтальная прокрутка.
        return Math.Max(column.Width, available - column.OtherWidth - 1.0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
