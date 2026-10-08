using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using TextElement = System.Windows.Documents.TextElement;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Views;

/// <summary>
/// Строка справочной таблицы, которая рисует свои клетки сама. Раньше каждая клетка
/// была шаблоном из рамки, сетки, подписи и иконок — на экране это тысячи элементов,
/// и на прокрутке их создание было самой дорогой частью. Здесь строка — один элемент:
/// при прокрутке меняются только данные, а рисование обходится в доли миллисекунды.
/// Правка и меню остаются прежними: клик переводится в клетку по координатам.
/// </summary>
public sealed class TableRowView : FrameworkElement
{
    /// <summary>Высота строки; у шапки и строки средних своя разметка.</summary>
    public const double RowHeight = 28;

    // Кисти и вид клеток берутся из ресурсов так же, как через DynamicResource:
    // смена темы или настроек перерисовывает строку сразу.
    public static readonly DependencyProperty GridBrushProperty = Register<Brush?>("GridBrush", null);
    public static readonly DependencyProperty TextBrushProperty = Register<Brush?>("TextBrush", Brushes.Black);
    public static readonly DependencyProperty SubtleBrushProperty = Register<Brush?>("SubtleBrush", Brushes.Gray);
    public static readonly DependencyProperty ManualOutlineProperty = Register<Brush?>("ManualOutline", null);
    public static readonly DependencyProperty CalculatorOutlineProperty = Register<Brush?>("CalculatorOutline", null);
    public static readonly DependencyProperty IconVisibilityProperty = Register("IconVisibility", Visibility.Visible);
    public static readonly DependencyProperty DamageAlignmentProperty = Register("DamageAlignment", TextAlignment.Left);
    public static readonly DependencyProperty DamagePaddingProperty = Register("DamagePadding", new Thickness(8, 4, 40, 4));

    /// <summary>Иконки общие на все строки: картинок всего десяток, а клеток — тысячи.</summary>
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

        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        DataContextChanged += (_, _) => Attach(DataContext as TableRowViewModel);
        Loaded += (_, _) => Attach(DataContext as TableRowViewModel);
        Unloaded += (_, _) => Attach(null);
    }

    /// <summary>Строка, которую сейчас показывает элемент; при прокрутке он переходит к другой.</summary>
    public TableRowViewModel? Row => _row;

    /// <summary>Клетка под точкой и её границы в координатах строки.</summary>
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

    /// <summary>Границы клетки в координатах строки.</summary>
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

        // Прозрачная подложка: иначе щелчок мимо текста проходил бы сквозь строку.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        Brush? grid = (Brush?)GetValue(GridBrushProperty);
        double x = 0;

        foreach (TableCell cell in _row.Cells)
        {
            double width = cell.Column.ActualWidth;
            Rect bounds = new(x, 0, width, RowHeight);

            DrawCell(dc, cell, bounds, typeface, pixelsPerDip);

            // Линии сетки — по правому и нижнему краю клетки, как было у рамок.
            // Направляющие ставят их точно на пиксели: иначе на 125% они расплывались бы.
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

        // Над выбором из списка — рука, как у кнопки: клик раскрывает список.
        TableCell? cell = CellAt(e.GetPosition(this), out _);
        Cursor = cell?.Column.Kind == TableCellKind.Options ? Cursors.Hand : null;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TableRowAutomationPeer(this);

    private void DrawCell(DrawingContext dc, TableCell cell, Rect bounds, Typeface typeface, double pixelsPerDip)
    {
        Brush text = (Brush?)GetValue(TextBrushProperty) ?? Brushes.Black;

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

            // Sin Cost и DPSC — просто числа по центру: без меток и обводки.
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
    /// Клетка урона: обводка по происхождению, число и иконки типа и греха справа.
    /// Не прошедшая фильтр клетка остаётся пустой — сетка на месте, значения нет.
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

        // Выключенная обводка — кисть с нулевой прозрачностью; её и рисовать незачем.
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
    /// Размер шрифта клетки: свой у столбца, если задан, иначе как у таблицы вокруг —
    /// он приходит от вкладки, как приходил к прежним подписям клеток.
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
            // Длинное название обрезается многоточием, а не вылезает в соседнюю клетку.
            Trimming = TextTrimming.CharacterEllipsis,
        };

        // По центру высоты без нижней линии сетки — так стоял текст в прежних клетках.
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
    /// Переходит к другой строке: отписывается от прежних клеток и столбцов,
    /// подписывается на новые. Элемент переиспользуется при прокрутке, поэтому
    /// подписки нельзя оставлять висеть.
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

        // Экранному чтецу — новое содержимое. Посредник есть, только пока его кто-то
        // спрашивал, так что без чтеца это ничего не стоит.
        UIElementAutomationPeer.FromElement(this)?.InvalidatePeer();
    }

    private void OnColumnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableColumn.ActualWidth))
        {
            InvalidateMeasure();
            InvalidateVisual();
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
/// Строка для экранных чтецов и автоматизации: своих элементов у клеток больше нет,
/// поэтому строка сама рассказывает о них — по подписи на клетку с её местом на экране.
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
            // Скрытое фильтром и пустое не показываем — на экране его тоже нет.
            if (cell.IsVisible && !string.IsNullOrEmpty(cell.Value))
            {
                cells.Add(new TableCellAutomationPeer(row, cell));
            }
        }

        return cells;
    }
}

/// <summary>Одна нарисованная клетка как текстовый элемент для автоматизации.</summary>
internal sealed class TableCellAutomationPeer(TableRowView row, TableCell cell) : AutomationPeer
{
    protected override string GetNameCore() => cell.Value;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => "TableCell";

    protected override string GetAutomationIdCore() => cell.Column.Key;

    protected override Rect GetBoundingRectangleCore()
    {
        // Строку могли уже переиспользовать под другие данные — тогда клетки в ней нет,
        // и границ у неё тоже нет. Бросать отсюда нельзя: сорвался бы весь запрос дерева.
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
