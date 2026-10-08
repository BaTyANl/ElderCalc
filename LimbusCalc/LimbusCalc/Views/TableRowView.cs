using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TextElement = System.Windows.Documents.TextElement;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Views;

/// <summary>
/// A table row that draws its own cells. A template per cell (border, grid, text, icons)
/// meant thousands of elements on screen, and creating them was the most expensive part of
/// scrolling. Here a row is a single element: scrolling only swaps the data, and drawing
/// takes a fraction of a millisecond. Clicks are mapped to cells by coordinates.
/// </summary>
public sealed class TableRowView : FrameworkElement
{
    /// <summary>Row height; the header and the averages row have their own markup.</summary>
    public const double RowHeight = 28;

    // Brushes and the cell look come from resources the same way DynamicResource works,
    // so a theme or settings change repaints the row immediately.
    public static readonly DependencyProperty GridBrushProperty = Register<Brush?>("GridBrush", null);
    public static readonly DependencyProperty TextBrushProperty = Register<Brush?>("TextBrush", Brushes.Black);
    public static readonly DependencyProperty SubtleBrushProperty = Register<Brush?>("SubtleBrush", Brushes.Gray);
    public static readonly DependencyProperty ManualOutlineProperty = Register<Brush?>("ManualOutline", null);
    public static readonly DependencyProperty CalculatorOutlineProperty = Register<Brush?>("CalculatorOutline", null);
    public static readonly DependencyProperty IconVisibilityProperty = Register("IconVisibility", Visibility.Visible);
    public static readonly DependencyProperty DamageAlignmentProperty = Register("DamageAlignment", TextAlignment.Left);
    public static readonly DependencyProperty DamagePaddingProperty = Register("DamagePadding", new Thickness(8, 4, 40, 4));
    public static readonly DependencyProperty HeatBrushProperty = Register<Brush?>("HeatBrush", null);
    public static readonly DependencyProperty HeatVisibilityProperty = Register("HeatVisibility", Visibility.Collapsed);

    /// <summary>Icons are shared by all rows: there are a dozen images but thousands of cells.</summary>
    private static readonly Dictionary<string, ImageSource> Icons = [];

    private static readonly Geometry Chevron = Geometry.Parse("M0,0 L4,4 L8,0");

    private TableRowViewModel? _row;
    private readonly HashSet<TableColumn> _columns = [];

    public TableRowView()
    {
        SetResourceReference(GridBrushProperty, "OutlineBrush");
        SetResourceReference(TextBrushProperty, "TextBrush");
        SetResourceReference(SubtleBrushProperty, "SubtleTextBrush");
        SetResourceReference(ManualOutlineProperty, SettingsViewModel.ManualOutlineKey);
        SetResourceReference(CalculatorOutlineProperty, SettingsViewModel.CalculatorOutlineKey);
        SetResourceReference(IconVisibilityProperty, SettingsViewModel.SkillIconVisibilityKey);
        SetResourceReference(DamageAlignmentProperty, SettingsViewModel.DamageAlignmentKey);
        SetResourceReference(DamagePaddingProperty, SettingsViewModel.DamagePaddingKey);
        SetResourceReference(HeatBrushProperty, "HeatBrush");
        SetResourceReference(HeatVisibilityProperty, SettingsViewModel.DamageScaleVisibilityKey);

        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        DataContextChanged += (_, _) => Attach(DataContext as TableRowViewModel);
        Loaded += (_, _) => Attach(DataContext as TableRowViewModel);
        Unloaded += (_, _) => Attach(null);
    }

    /// <summary>The row this element shows right now; it moves to another one when scrolling.</summary>
    public TableRowViewModel? Row => _row;

    /// <summary>The cell under the point and its bounds in row coordinates.</summary>
    public TableCell? CellAt(Point point, out Rect bounds)
    {
        bounds = Rect.Empty;

        if (_row is null || point.Y < 0 || point.Y > RowHeight)
        {
            return null;
        }

        double x = 0;

        foreach (TableCell cell in _row.Cells)
        {
            double width = cell.Column.ActualWidth;

            if (point.X >= x && point.X < x + width)
            {
                bounds = new Rect(x, 0, width, RowHeight);
                return cell;
            }

            x += width;
        }

        return null;
    }

    /// <summary>A cell's bounds in row coordinates.</summary>
    public Rect BoundsOf(TableCell target)
    {
        double x = 0;

        foreach (TableCell cell in _row?.Cells ?? [])
        {
            if (ReferenceEquals(cell, target))
            {
                return new Rect(x, 0, cell.Column.ActualWidth, RowHeight);
            }

            x += cell.Column.ActualWidth;
        }

        return Rect.Empty;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;

        foreach (TableCell cell in _row?.Cells ?? [])
        {
            width += cell.Column.ActualWidth;
        }

        return new Size(width, RowHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_row is null)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Typeface typeface = new(
            TextElement.GetFontFamily(this),
            TextElement.GetFontStyle(this),
            TextElement.GetFontWeight(this),
            TextElement.GetFontStretch(this));

        // A transparent background, otherwise clicks between texts would fall through the row.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        Brush? grid = (Brush?)GetValue(GridBrushProperty);
        double x = 0;

        foreach (TableCell cell in _row.Cells)
        {
            double width = cell.Column.ActualWidth;

            // A hidden column takes no space and has nothing to draw.
            if (width <= 0)
            {
                continue;
            }

            Rect bounds = new(x, 0, width, RowHeight);

            DrawCell(dc, cell, bounds, typeface, pixelsPerDip);

            // Grid lines along the right and bottom edge of each cell. Guidelines snap them to
            // pixels; without them they'd blur at 125% scaling.
            if (grid is not null)
            {
                GuidelineSet guides = new(
                    [bounds.Right - 1, bounds.Right],
                    [bounds.Bottom - 1, bounds.Bottom]);

                dc.PushGuidelineSet(guides);
                dc.DrawRectangle(grid, null, new Rect(bounds.Right - 1, 0, 1, RowHeight));
                dc.DrawRectangle(grid, null, new Rect(bounds.X, bounds.Bottom - 1, width, 1));
                dc.Pop();
            }

            x += width;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // A hand over list cells, like on a button: a click opens the list.
        TableCell? cell = CellAt(e.GetPosition(this), out _);
        Cursor = cell?.Column.Kind == TableCellKind.Options ? Cursors.Hand : null;

        // Each cell has its own tooltip but the element covers the whole row. A regular
        // element tooltip wouldn't change between cells, so the tooltip is managed here.
        if (ReferenceEquals(_tipView, this) && ReferenceEquals(_tipCell, cell))
        {
            return;
        }

        HideTip();

        if (cell is not null)
        {
            _tipView = this;
            _tipCell = cell;
            TipDelay.Start();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (ReferenceEquals(_tipView, this))
        {
            HideTip();
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);

        // A click opens an editor or a menu; the tooltip would only get in the way.
        HideTip();
    }

    /// <summary>One tooltip for all rows: only one is ever visible.</summary>
    private static readonly ToolTip Tip = new() { Placement = PlacementMode.Bottom };

    /// <summary>A delay like regular tooltips have, so they don't flicker as the mouse passes.</summary>
    private static readonly DispatcherTimer TipDelay = CreateTipDelay();

    private static TableRowView? _tipView;
    private static TableCell? _tipCell;

    private static DispatcherTimer CreateTipDelay()
    {
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) => ShowTip();
        return timer;
    }

    /// <summary>Hides the tooltip — on scroll, click, or when the mouse leaves the cell.</summary>
    public static void HideTip()
    {
        TipDelay.Stop();
        Tip.IsOpen = false;
        _tipView = null;
        _tipCell = null;
    }

    private static void ShowTip()
    {
        TipDelay.Stop();

        if (_tipView is not { IsMouseOver: true } view
            || _tipCell is not TableCell cell
            || view.BoundsOf(cell) is not { IsEmpty: false } bounds
            || view.TipFor(cell, bounds) is not string text)
        {
            return;
        }

        Tip.Content = text;
        Tip.PlacementTarget = view;
        Tip.PlacementRectangle = bounds;
        Tip.IsOpen = true;
    }

    /// <summary>
    /// What to show over a cell: the setup summary, the DPSC calculation, or the full name
    /// when it's cut off with an ellipsis.
    /// </summary>
    private string? TipFor(TableCell cell, Rect bounds)
    {
        if (CellTips.Describe(cell) is string described)
        {
            return described;
        }

        double room = cell.Column.Kind switch
        {
            TableCellKind.Text => bounds.Width - 16,
            TableCellKind.Options => bounds.Width - 30,
            _ => double.PositiveInfinity,
        };

        return !cell.IsEmpty && TextWidth(cell.Value, SizeOf(cell)) > room ? cell.Value : null;
    }

    private double TextWidth(string value, double fontSize) =>
        new FormattedText(
            value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(
                TextElement.GetFontFamily(this),
                TextElement.GetFontStyle(this),
                TextElement.GetFontWeight(this),
                TextElement.GetFontStretch(this)),
            fontSize,
            Brushes.Black,
            null,
            TextFormattingMode.Ideal,
            VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;

    protected override AutomationPeer OnCreateAutomationPeer() => new TableRowAutomationPeer(this);

    private void DrawCell(DrawingContext dc, TableCell cell, Rect bounds, Typeface typeface, double pixelsPerDip)
    {
        Brush text = (Brush?)GetValue(TextBrushProperty) ?? Brushes.Black;

        DrawHeat(dc, cell, bounds);

        switch (cell.Column.Kind)
        {
            case TableCellKind.Options:
                DrawText(dc, cell.Value, new Rect(bounds.X + 8, 0, bounds.Width - 30, RowHeight),
                    TextAlignment.Left, SizeOf(cell), text, typeface, pixelsPerDip);
                DrawChevron(dc, bounds);
                break;

            case TableCellKind.Integer when cell.Column.AcceptsSetup:
                DrawDamage(dc, cell, bounds, text, typeface, pixelsPerDip);
                break;

            // Sin Cost and DPSC are plain centered numbers: no marks and no outline.
            case TableCellKind.Integer:
            case TableCellKind.Computed:
                if (cell.IsVisible)
                {
                    DrawText(dc, cell.Value, new Rect(bounds.X + 6, 0, bounds.Width - 12, RowHeight),
                        TextAlignment.Center, SizeOf(cell), text, typeface, pixelsPerDip);
                }

                break;

            default:
                DrawText(dc, cell.Value, new Rect(bounds.X + 8, 0, bounds.Width - 16, RowHeight),
                    TextAlignment.Left, SizeOf(cell), text, typeface, pixelsPerDip);
                break;
        }
    }

    /// <summary>
    /// Fill by the color scale: the higher the value in its column, the stronger the fill.
    /// Even the lowest gets a light tint, so it's clear the cell is on the scale.
    /// </summary>
    private void DrawHeat(DrawingContext dc, TableCell cell, Rect bounds)
    {
        if (!cell.Column.HasScale
            || !cell.IsVisible
            || (Visibility)GetValue(HeatVisibilityProperty) != Visibility.Visible
            || GetValue(HeatBrushProperty) is not Brush heat
            || cell.Number is not double value
            || cell.Column.ScaleOf(value) is not double share)
        {
            return;
        }

        // Stops short of the bottom and right grid lines so the fill doesn't cover them.
        dc.PushOpacity(0.05 + 0.5 * share);
        dc.DrawRectangle(heat, null, new Rect(bounds.X, 0, bounds.Width - 1, RowHeight - 1));
        dc.Pop();
    }

    /// <summary>
    /// A damage cell: outline by origin, the number, and type and sin icons on the right.
    /// A cell filtered out stays empty — the grid is there, the value isn't.
    /// </summary>
    private void DrawDamage(DrawingContext dc, TableCell cell, Rect bounds, Brush text, Typeface typeface, double pixelsPerDip)
    {
        if (!cell.IsVisible)
        {
            return;
        }

        Brush? outline = cell.Source switch
        {
            TableCellSource.Manual => (Brush?)GetValue(ManualOutlineProperty),
            TableCellSource.Calculator => (Brush?)GetValue(CalculatorOutlineProperty),
            _ => null,
        };

        // A disabled outline is a brush with zero opacity; no point drawing it.
        if (outline is not null && outline.Opacity > 0)
        {
            Pen pen = new(outline, 1.5);
            Rect frame = new(bounds.X + 2.75, 2.75, bounds.Width - 5.5, RowHeight - 5.5);
            dc.DrawRoundedRectangle(null, pen, frame, 4, 4);
        }

        Thickness padding = (Thickness)GetValue(DamagePaddingProperty);
        DrawText(
            dc,
            cell.Value,
            new Rect(bounds.X + padding.Left, 0, bounds.Width - padding.Left - padding.Right, RowHeight),
            (TextAlignment)GetValue(DamageAlignmentProperty),
            SizeOf(cell),
            text,
            typeface,
            pixelsPerDip);

        if ((Visibility)GetValue(IconVisibilityProperty) != Visibility.Visible)
        {
            return;
        }

        double top = Math.Round((RowHeight - 15) / 2);
        DrawIcon(dc, cell.SkillType?.IconPath, new Rect(bounds.Right - 21 - 15, top, 15, 15));
        DrawIcon(dc, cell.SkillSin?.IconPath, new Rect(bounds.Right - 4 - 15, top, 15, 15));
    }

    /// <summary>
    /// The cell font size: the column's own if set, otherwise the table's, which is
    /// inherited from the tab.
    /// </summary>
    private double SizeOf(TableCell cell) => cell.Column.FontSize ?? TextElement.GetFontSize(this);

    private static void DrawText(
        DrawingContext dc,
        string value,
        Rect area,
        TextAlignment alignment,
        double fontSize,
        Brush brush,
        Typeface typeface,
        double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(value) || area.Width <= 0)
        {
            return;
        }

        FormattedText formatted = new(
            value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            null,
            TextFormattingMode.Ideal,
            pixelsPerDip)
        {
            MaxTextWidth = area.Width,
            MaxLineCount = 1,
            TextAlignment = alignment,
            // A long name is cut off with an ellipsis instead of spilling into the next cell.
            Trimming = TextTrimming.CharacterEllipsis,
        };

        // Vertically centered, not counting the bottom grid line.
        double y = Math.Round((RowHeight - 1 - formatted.Height) / 2);
        dc.DrawText(formatted, new Point(area.X, y));
    }

    private void DrawChevron(DrawingContext dc, Rect bounds)
    {
        if (GetValue(SubtleBrushProperty) is not Brush brush)
        {
            return;
        }

        dc.PushTransform(new TranslateTransform(bounds.Right - 16, Math.Round((RowHeight - 1 - 4) / 2)));
        dc.DrawGeometry(null, new Pen(brush, 1.4), Chevron);
        dc.Pop();
    }

    private static void DrawIcon(DrawingContext dc, string? path, Rect area)
    {
        if (path is null)
        {
            return;
        }

        if (!Icons.TryGetValue(path, out ImageSource? image))
        {
            BitmapImage loaded = new(new Uri(path));
            loaded.Freeze();
            image = loaded;
            Icons[path] = image;
        }

        dc.DrawImage(image, area);
    }

    /// <summary>
    /// Switches to another row: unsubscribes from the old cells and columns and subscribes
    /// to the new ones. The element is reused while scrolling, so subscriptions must not
    /// be left behind.
    /// </summary>
    private void Attach(TableRowViewModel? row)
    {
        if (ReferenceEquals(row, _row))
        {
            return;
        }

        if (_row is not null)
        {
            foreach (TableCell cell in _row.Cells)
            {
                cell.PropertyChanged -= OnCellChanged;
            }
        }

        foreach (TableColumn column in _columns)
        {
            column.PropertyChanged -= OnColumnChanged;
        }

        _columns.Clear();
        _row = row;

        if (_row is not null)
        {
            foreach (TableCell cell in _row.Cells)
            {
                cell.PropertyChanged += OnCellChanged;

                if (_columns.Add(cell.Column))
                {
                    cell.Column.PropertyChanged += OnColumnChanged;
                }
            }
        }

        InvalidateMeasure();
        InvalidateVisual();
        UIElementAutomationPeer.FromElement(this)?.InvalidatePeer();
    }

    private void OnCellChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();

        // Let a screen reader know about the new content. The peer exists only once something
        // has asked for it, so without a screen reader this costs nothing.
        UIElementAutomationPeer.FromElement(this)?.InvalidatePeer();
    }

    private void OnColumnChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TableColumn.ActualWidth):
                InvalidateMeasure();
                InvalidateVisual();
                break;

            // The scale bounds moved, so every cell of the column gets a different fill.
            case nameof(TableColumn.Scale):
                InvalidateVisual();
                break;
        }
    }

    private static DependencyProperty Register<T>(string name, T fallback) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(TableRowView),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));
}

/// <summary>
/// The row for screen readers and UI automation: cells have no elements of their own, so the
/// row describes them — one text item per cell with its place on screen.
/// </summary>
internal sealed class TableRowAutomationPeer(TableRowView owner) : FrameworkElementAutomationPeer(owner)
{
    protected override string GetClassNameCore() => nameof(TableRowView);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;

    protected override List<AutomationPeer>? GetChildrenCore()
    {
        TableRowView row = (TableRowView)Owner;

        if (row.Row is null)
        {
            return null;
        }

        List<AutomationPeer> cells = [];

        foreach (TableCell cell in row.Row.Cells)
        {
            // Skip filtered-out and empty cells — they aren't visible on screen either.
            if (cell.IsVisible && !string.IsNullOrEmpty(cell.Value))
            {
                cells.Add(new TableCellAutomationPeer(row, cell));
            }
        }

        return cells;
    }
}

/// <summary>One drawn cell exposed as a text element to UI automation.</summary>
internal sealed class TableCellAutomationPeer(TableRowView row, TableCell cell) : AutomationPeer
{
    protected override string GetNameCore() => cell.Value;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => "TableCell";

    protected override string GetAutomationIdCore() => cell.Column.Key;

    protected override Rect GetBoundingRectangleCore()
    {
        // The row may already be reused for other data — then the cell isn't in it and has no
        // bounds. Throwing here isn't an option: it would break the whole tree query.
        Rect bounds = row.BoundsOf(cell);

        if (bounds.IsEmpty || PresentationSource.FromVisual(row) is null)
        {
            return Rect.Empty;
        }

        return new Rect(row.PointToScreen(bounds.TopLeft), row.PointToScreen(bounds.BottomRight));
    }

    protected override bool IsOffscreenCore() =>
        PresentationSource.FromVisual(row) is null || !row.IsVisible || row.BoundsOf(cell).IsEmpty;

    protected override Point GetClickablePointCore()
    {
        Rect bounds = GetBoundingRectangleCore();
        return bounds.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    protected override List<AutomationPeer>? GetChildrenCore() => null;

    protected override string GetAcceleratorKeyCore() => string.Empty;

    protected override string GetAccessKeyCore() => string.Empty;

    protected override string GetHelpTextCore() => string.Empty;

    protected override string GetItemStatusCore() => string.Empty;

    protected override string GetItemTypeCore() => string.Empty;

    protected override AutomationPeer? GetLabeledByCore() => null;

    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;

    public override object? GetPattern(PatternInterface patternInterface) => null;

    protected override bool HasKeyboardFocusCore() => false;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsEnabledCore() => true;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override bool IsPasswordCore() => false;

    protected override bool IsRequiredForFormCore() => false;

    protected override void SetFocusCore()
    {
    }
}
