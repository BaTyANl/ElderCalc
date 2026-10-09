using System.IO;
using System.Windows;
using System.Windows.Controls;
using TextElement = System.Windows.Documents.TextElement;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Views;

/// <summary>
/// A picture of a table exactly as it looks on screen: title, header, the rows the filters
/// show and the averages row. Built off screen from the same row elements, so the picture
/// matches the window, theme included.
/// </summary>
public static class TableImage
{
    /// <summary>
    /// Puts the picture on the clipboard, both as a bitmap and as PNG: chat apps such as
    /// Discord take the PNG, older programs the bitmap. <paramref name="scale"/> is the
    /// screen's DPI scale, so the picture is as sharp as the window.
    /// </summary>
    public static void CopyToClipboard(TableViewModel table, double scale)
    {
        BitmapSource image = Render(table, scale);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(image));

        MemoryStream png = new();
        encoder.Save(png);
        png.Position = 0;

        DataObject data = new();
        data.SetImage(image);
        data.SetData("PNG", png);
        Clipboard.SetDataObject(data, copy: true);
    }

    /// <summary>Draws the table off screen.</summary>
    public static BitmapSource Render(TableViewModel table, double scale)
    {
        ArgumentNullException.ThrowIfNull(table);

        StackPanel content = new() { Orientation = Orientation.Vertical };

        TextBlock title = new()
        {
            Text = table.FilterSummary.Length == 0 ? table.Title : $"{table.Title} · {table.FilterSummary}",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 0, 10),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        content.Children.Add(title);

        content.Children.Add(Line(table.DisplayColumns.Select(column =>
            (column, column.Title + (column.Indicator.Length == 0 ? string.Empty : " " + column.Indicator))),
            isHeader: true));

        // Rows draw their right and bottom lines themselves; the left edge is a border, as in the window.
        StackPanel rows = new() { Orientation = Orientation.Vertical };

        foreach (TableRowViewModel row in table.Rows.Where(row => row.IsVisible))
        {
            rows.Children.Add(new TableRowView { DataContext = row });
        }

        Border body = new()
        {
            BorderThickness = new Thickness(1, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = rows,
        };
        body.SetResourceReference(Border.BorderBrushProperty, "OutlineBrush");
        content.Children.Add(body);

        content.Children.Add(Line(table.Averages.Select(average => (average.Column, average.Text)), isHeader: false));

        Border root = new()
        {
            Padding = new Thickness(16),
            Child = content,
        };
        root.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        root.SetResourceReference(TextElement.FontFamilyProperty, "AppFontFamily");
        TextElement.SetFontSize(root, 14);

        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();

        RenderTargetBitmap bitmap = new(
            (int)Math.Ceiling(root.ActualWidth * scale),
            (int)Math.Ceiling(root.ActualHeight * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>The header or the averages row: one bordered cell per shown column.</summary>
    private static Border Line(IEnumerable<(TableColumn Column, string Text)> cells, bool isHeader)
    {
        StackPanel line = new() { Orientation = Orientation.Horizontal };

        foreach ((TableColumn column, string text) in cells)
        {
            if (column.IsHidden)
            {
                continue;
            }

            bool centered = column.Kind is TableCellKind.Integer or TableCellKind.Computed or TableCellKind.Favorite;

            TextBlock label = new()
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                FontSize = isHeader ? 15 : 14,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, isHeader ? "TextBrush" : "SubtleTextBrush");

            Border cell = new()
            {
                Width = column.ActualWidth,
                Padding = new Thickness(8, 6, 8, 6),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = label,
            };
            cell.SetResourceReference(Border.BorderBrushProperty, "OutlineBrush");
            line.Children.Add(cell);
        }

        // A thicker line under the header and over the averages, as in the window.
        Border frame = new()
        {
            BorderThickness = isHeader ? new Thickness(1, 1, 0, 2) : new Thickness(1, 2, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = line,
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "OutlineBrush");
        return frame;
    }
}
