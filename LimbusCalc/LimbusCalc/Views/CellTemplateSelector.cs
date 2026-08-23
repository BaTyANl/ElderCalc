using System.Windows;
using System.Windows.Controls;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Views;

/// <summary>
/// Выбирает разметку клетки по виду её столбца. Раньше это делал ContentControl
/// со стилем и парой условий — лишний элемент и лишние привязки в каждой клетке,
/// а их на экране сотни.
/// </summary>
public sealed class CellTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Text { get; set; }

    public DataTemplate? Integer { get; set; }

    public DataTemplate? Options { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is not TableCell cell
            ? base.SelectTemplate(item, container)
            : cell.Column.Kind switch
            {
                TableCellKind.Integer => Integer,
                TableCellKind.Options => Options,
                _ => Text,
            };
}
