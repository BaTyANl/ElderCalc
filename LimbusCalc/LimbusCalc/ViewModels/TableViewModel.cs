using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace LimbusCalc.ViewModels;

/// <summary>Чем заполняется клетка столбца.</summary>
public enum TableCellKind
{
    /// <summary>Свободный текст.</summary>
    Text,

    /// <summary>Только целое число.</summary>
    Integer,

    /// <summary>Выбор из готового списка значений.</summary>
    Options,
}

/// <summary>Откуда в клетке взялся урон.</summary>
public enum TableCellSource
{
    /// <summary>Пусто: урона нет.</summary>
    Empty,

    /// <summary>Значение вписано руками.</summary>
    Manual,

    /// <summary>Значение выгружено из калькулятора, и набор лежит вместе с ним.</summary>
    Calculator,
}

/// <summary>Чем сравниваются клетки скиллов при сортировке.</summary>
public enum SkillSortKey
{
    Damage,
    Type,
    Sin,
}

/// <summary>Пункт списка приоритетов сортировки: сам признак и его подпись.</summary>
public sealed class SkillSortOption
{
    public required SkillSortKey Key { get; init; }

    public required string Name { get; init; }
}

/// <summary>Столбец справочной таблицы: подпись в шапке, ширина и вид клеток.</summary>
public sealed class TableColumn : ObservableObject
{
    private string _indicator = string.Empty;
    private double? _actualWidth;

    public required string Title { get; init; }

    /// <summary>Ширина столбца; у растяжимого — наименьшая допустимая.</summary>
    public required double Width { get; init; }

    public TableCellKind Kind { get; init; } = TableCellKind.Text;

    /// <summary>Варианты для <see cref="TableCellKind.Options"/>; у прочих пусто.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>
    /// Прежние названия столбца. Нужны при чтении файлов: столбец могли переименовать
    /// уже после того, как выгрузку сохранили, и терять из-за этого данные незачем.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Отзывается ли столбец на это название — своё или прежнее.</summary>
    public bool Matches(string title) =>
        Title == title || Aliases.Contains(title);

    /// <summary>Столбец попадает в выпадающие списки, и там его подписывают этим.</summary>
    public override string ToString() => Title;

    /// <summary>Столбец забирает всю ширину, не занятую остальными.</summary>
    public bool Stretch { get; init; }

    /// <summary>
    /// Сколько занимают остальные столбцы. Считается один раз при сборке таблицы —
    /// растяжимому столбцу этого хватает, чтобы вычислить свою ширину по ширине окна.
    /// </summary>
    public double OtherWidth { get; private set; }

    /// <summary>Стрелка направления у столбца, по которому сейчас сортируем.</summary>
    public string Indicator
    {
        get => _indicator;
        internal set => SetProperty(ref _indicator, value);
    }

    /// <summary>
    /// Ширина, с которой столбец рисуется сейчас: у растяжимого она зависит от окна.
    /// Клетки берут её отсюда — так шапка, строки и строка средних всегда согласованы,
    /// откуда бы ни считалась ширина видимой области.
    /// </summary>
    public double ActualWidth
    {
        get => _actualWidth ?? Width;
        internal set
        {
            if (_actualWidth != value)
            {
                _actualWidth = value;
                OnPropertyChanged();
            }
        }
    }

    internal static void MeasureStretch(IReadOnlyList<TableColumn> columns)
    {
        foreach (TableColumn column in columns)
        {
            if (column.Stretch)
            {
                column.OtherWidth = columns.Where(other => other != column).Sum(other => other.Width);
            }
        }
    }
}

/// <summary>
/// Клетка таблицы. Значение хранится текстом: пустая клетка — это именно пусто,
/// а не ноль, и при сортировке такие уходят вниз. У клетки скилла можно указать
/// тип урона и грех — по ним тоже сортируют.
/// </summary>
public sealed class TableCell : ObservableObject
{
    private string _value = string.Empty;
    private string? _setup;
    private ElementOption? _skillType;
    private ElementOption? _skillSin;
    private bool _isVisible = true;

    /// <summary>Столбец, которому клетка принадлежит: из него берётся ширина и вид.</summary>
    public required TableColumn Column { get; init; }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(Source));
            }
        }
    }

    /// <summary>
    /// Набор калькулятора, из которого получено значение, как он лежит в файле.
    /// Пусто — значение вписано руками; вернуть такое в калькулятор нечем.
    /// </summary>
    public string? Setup
    {
        get => _setup;
        set
        {
            if (SetProperty(ref _setup, value))
            {
                OnPropertyChanged(nameof(HasSetup));
                OnPropertyChanged(nameof(CanEditMarks));
                OnPropertyChanged(nameof(Source));
            }
        }
    }

    /// <summary>Есть ли что вернуть в калькулятор.</summary>
    public bool HasSetup => !string.IsNullOrEmpty(Setup);

    /// <summary>
    /// Можно ли назначать тип и грех вручную. У клетки с набором они приезжают
    /// из калькулятора вместе с уроном, и править их отдельно нечего.
    /// </summary>
    public bool CanEditMarks => !HasSetup;

    /// <summary>
    /// Откуда взялся урон. Клетку с набором руками не правят: иначе число и набор
    /// разошлись бы, и было бы неясно, что именно вернётся в калькулятор.
    /// </summary>
    public TableCellSource Source =>
        IsEmpty ? TableCellSource.Empty
        : HasSetup ? TableCellSource.Calculator
        : TableCellSource.Manual;

    /// <summary>Тип урона скилла; не задан — клетка просто число.</summary>
    public ElementOption? SkillType
    {
        get => _skillType;
        set => SetProperty(ref _skillType, value);
    }

    /// <summary>Грех скилла.</summary>
    public ElementOption? SkillSin
    {
        get => _skillSin;
        set => SetProperty(ref _skillSin, value);
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    /// <summary>
    /// Проходит ли значение через фильтр. Не прошедшая клетка остаётся пустой:
    /// её урон не показывают и в среднее не берут.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetProperty(ref _isVisible, value);
    }

    /// <summary>Число клетки или пусто, если там не число.</summary>
    public double? Number =>
        double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
}

/// <summary>Строка таблицы: по клетке на столбец, в том же порядке.</summary>
public sealed class TableRowViewModel : ObservableObject
{
    private bool _isVisible = true;

    public required IReadOnlyList<TableCell> Cells { get; init; }

    /// <summary>Проходит ли строка через фильтр.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetProperty(ref _isVisible, value);
    }

    /// <summary>Клетка нужного столбца или пусто, если такого столбца нет.</summary>
    public TableCell? CellOf(TableColumn column)
    {
        foreach (TableCell cell in Cells)
        {
            if (cell.Column == column)
            {
                return cell;
            }
        }

        return null;
    }

    /// <summary>
    /// То же по названию столбца — так строка читается из файла. Прежние названия
    /// столбца тоже подходят, иначе старые выгрузки теряли бы столбец.
    /// </summary>
    public TableCell? CellOf(string columnTitle)
    {
        foreach (TableCell cell in Cells)
        {
            if (cell.Column.Matches(columnTitle))
            {
                return cell;
            }
        }

        return null;
    }
}

/// <summary>
/// Клетка итоговой строки под таблицей. Стоит в том же столбце, что и данные,
/// поэтому ширину берёт оттуда же.
/// </summary>
public sealed class TableAverage : ObservableObject
{
    private string _text = string.Empty;

    public required TableColumn Column { get; init; }

    public string Text
    {
        get => _text;
        internal set => SetProperty(ref _text, value);
    }
}

/// <summary>
/// Справочная таблица с заданным набором столбцов. Общая и для ID, и для E.G.O.:
/// отличаются они только столбцами, поведение одно.
/// </summary>
public sealed class TableViewModel : ObservableObject
{
    private TableColumn? _sortColumn;
    private bool _sortDescending;
    private IReadOnlyList<TableAverage>? _averages;
    private int _bulkDepth;
    private bool _bulkChanged;

    /// <summary>Подпись над таблицей.</summary>
    public required string Title { get; init; }

    public required IReadOnlyList<TableColumn> Columns { get; init; }

    /// <summary>Отбор строк и значений; создаётся вместе с таблицей.</summary>
    public required TableFilterViewModel Filter { get; init; }

    /// <summary>Есть ли столбец грешника — от него зависит, показывать ли их список.</summary>
    public bool HasSinners => Columns.Any(column => column.Title == "Sinner");

    /// <summary>Есть ли столбец редкости — от него зависит, показывать ли её фильтр.</summary>
    public bool HasRarity => Columns.Any(column => column.Title == "Rarity");

    public ObservableCollection<TableRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Чем сравнивать клетки скиллов, от главного признака к последнему.
    /// По умолчанию урон, затем тип, затем грех; порядок правится в меню столбца.
    /// </summary>
    public ObservableCollection<SkillSortOption> SortPriority { get; } =
    [
        new SkillSortOption { Key = SkillSortKey.Damage, Name = "Damage" },
        new SkillSortOption { Key = SkillSortKey.Type, Name = "Skill type" },
        new SkillSortOption { Key = SkillSortKey.Sin, Name = "Skill sin" },
    ];

    /// <summary>
    /// Строка под таблицей: средний урон по каждому столбцу скилла. Заводится по
    /// столбцам один раз, дальше только пересчитывается — чтобы привязка не рвалась.
    /// </summary>
    public IReadOnlyList<TableAverage> Averages
    {
        get
        {
            if (_averages is null)
            {
                _averages = [.. Columns.Select(column => new TableAverage { Column = column })];
                UpdateAverages();
            }

            return _averages;
        }
    }

    /// <summary>Есть ли что удалять — по этому свойству гаснет кнопка удаления.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Таблица пуста — вместо строк показываем подсказку.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// Содержимое изменилось: добавили или убрали строку, поправили клетку, пересортировали.
    /// По этому событию таблица уходит на диск.
    /// </summary>
    public event EventHandler? Changed;

    public TableRowViewModel AddRow()
    {
        TableRowViewModel row = new()
        {
            Cells = [.. Columns.Select(column => new TableCell { Column = column })],
        };

        foreach (TableCell cell in row.Cells)
        {
            cell.PropertyChanged += OnCellChanged;
        }

        Rows.Add(row);
        OnRowsChanged();
        return row;
    }

    /// <summary>Убирает строку целиком вместе с наборами, что лежали в её клетках.</summary>
    public void Remove(TableRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!Rows.Remove(row))
        {
            return;
        }

        foreach (TableCell cell in row.Cells)
        {
            cell.PropertyChanged -= OnCellChanged;
        }

        OnRowsChanged();
    }

    public void Clear()
    {
        using IDisposable bulk = BeginBulkChange();

        while (Rows.Count > 0)
        {
            Remove(Rows[^1]);
        }
    }

    /// <summary>
    /// Отсортировать по этому столбцу; повторное нажатие переворачивает порядок.
    /// Клетки без данных уходят вниз в обе стороны: там нечего сравнивать.
    /// </summary>
    public void SortBy(TableColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }

        ApplySort();
    }

    /// <summary>Двигает признак в списке приоритетов: -1 вверх, +1 вниз.</summary>
    public void MovePriority(SkillSortOption option, int delta)
    {
        int index = SortPriority.IndexOf(option);
        int target = index + delta;

        if (index < 0 || target < 0 || target >= SortPriority.Count)
        {
            return;
        }

        SortPriority.Move(index, target);

        // Порядок признаков поменялся — таблица должна перестроиться сразу.
        if (_sortColumn?.Kind == TableCellKind.Integer)
        {
            ApplySort();
        }
    }

    /// <summary>
    /// Пересчитывает ширины столбцов под видимую область: растяжимый забирает остаток.
    /// Вызывается при изменении размера — шапка, строки и средние берут ширины отсюда,
    /// поэтому остаются согласованными.
    /// </summary>
    public void UpdateColumnWidths(double viewportWidth)
    {
        foreach (TableColumn column in Columns)
        {
            // Единица — на внешнюю рамку таблицы, иначе строка вылезает за неё
            // и появляется лишняя горизонтальная прокрутка.
            column.ActualWidth = column.Stretch && viewportWidth > 0.0
                ? Math.Max(column.Width, viewportWidth - column.OtherWidth - 1.0)
                : column.Width;
        }
    }

    /// <summary>Пересортировать по текущему столбцу; без выбранного столбца ничего не делает.</summary>
    public void ApplySort()
    {
        UpdateIndicators();

        if (_sortColumn is null || Rows.Count < 2)
        {
            return;
        }

        TableColumn column = _sortColumn;
        RowComparer comparer = new(column, SortPriority);

        // Пустые клетки — не наименьшее значение, а отсутствие данных: они
        // остаются внизу независимо от направления.
        IEnumerable<TableRowViewModel> filled = Rows.Where(row => !IsCellEmpty(row, column));
        IEnumerable<TableRowViewModel> blank = Rows.Where(row => IsCellEmpty(row, column));

        List<TableRowViewModel> sorted =
        [
            .. _sortDescending
                ? filled.OrderByDescending(row => row, comparer)
                : filled.OrderBy(row => row, comparer),
            .. blank,
        ];

        for (int i = 0; i < sorted.Count; i++)
        {
            int current = Rows.IndexOf(sorted[i]);

            if (current != i)
            {
                Rows.Move(current, i);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsCellEmpty(TableRowViewModel row, TableColumn column) =>
        row.CellOf(column)?.IsEmpty ?? true;

    private void UpdateIndicators()
    {
        foreach (TableColumn column in Columns)
        {
            column.Indicator = column == _sortColumn
                ? _sortDescending ? "▼" : "▲"
                : string.Empty;
        }
    }

    /// <summary>
    /// Открывает пакетную правку: фильтр и средние пересчитываются один раз в конце,
    /// а не после каждой клетки. На загрузке справочника это разница в десятки раз —
    /// иначе каждая из тысяч правок заново обходит всю таблицу.
    /// </summary>
    public IDisposable BeginBulkChange()
    {
        _bulkDepth++;

        return new BulkScope(this);
    }

    private void EndBulkChange()
    {
        if (--_bulkDepth > 0 || !_bulkChanged)
        {
            return;
        }

        _bulkChanged = false;
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnCellChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Видимость клетке ставит сам фильтр. Это не правка данных: ни пересчитывать
        // фильтр заново, ни сохранять файл не нужно — а на тысяче клеток такой
        // повторный обход стоил секунд.
        if (e.PropertyName == nameof(TableCell.IsVisible))
        {
            return;
        }

        if (_bulkDepth > 0)
        {
            _bulkChanged = true;
            return;
        }

        // Правка метки или значения может вывести строку из-под фильтра.
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRowsChanged()
    {
        if (_bulkDepth > 0)
        {
            _bulkChanged = true;
            return;
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class BulkScope(TableViewModel table) : IDisposable
    {
        private bool _closed;

        public void Dispose()
        {
            if (!_closed)
            {
                _closed = true;
                table.EndBulkChange();
            }
        }
    }

    /// <summary>
    /// Прогоняет строки через фильтр: грешник отбирает строки целиком, тип и грех —
    /// отдельные значения. Строка, у которой не осталось ни одного подходящего
    /// значения, скрывается вся.
    /// </summary>
    public void ApplyFilter()
    {
        foreach (TableRowViewModel row in Rows)
        {
            // Грешник, редкость и поиск отбирают строку целиком, тип и грех — её значения.
            // Столбец названия у ID и E.G.O. называется по-разному, но откликается на «Name».
            bool rowOk = Filter.AllowsName(row.CellOf("Name")?.Value)
                && Filter.AllowsSinner(row.CellOf("Sinner")?.Value)
                && Filter.AllowsRarity(row.CellOf("Rarity")?.Value);

            bool anyValue = false;

            foreach (TableCell cell in row.Cells)
            {
                if (cell.Column.Kind != TableCellKind.Integer)
                {
                    cell.IsVisible = true;
                    continue;
                }

                cell.IsVisible = rowOk && Filter.AllowsMarks(cell.SkillType, cell.SkillSin);
                anyValue |= cell.IsVisible && !cell.IsEmpty;
            }

            row.IsVisible = rowOk && (!Filter.FiltersMarks || anyValue);
        }

        UpdateAverages();
    }

    /// <summary>
    /// Считает средний урон по столбцам скиллов. Пустые клетки в счёт не идут:
    /// незаполненный скилл — это не нулевой урон, и занижать им среднее незачем.
    /// </summary>
    private void UpdateAverages()
    {
        if (_averages is null)
        {
            return;
        }

        for (int i = 0; i < _averages.Count; i++)
        {
            TableAverage average = _averages[i];

            if (i == 0)
            {
                average.Text = "Average";
                continue;
            }

            if (average.Column.Kind != TableCellKind.Integer)
            {
                average.Text = string.Empty;
                continue;
            }

            double sum = 0.0;
            int count = 0;

            foreach (TableRowViewModel row in Rows)
            {
                // Среднее считается по тому, что видно: скрытое фильтром в счёт не идёт.
                if (!row.IsVisible)
                {
                    continue;
                }

                TableCell? cell = row.CellOf(average.Column);

                if (cell is { IsVisible: true, Number: double value })
                {
                    sum += value;
                    count++;
                }
            }

            average.Text = count == 0
                ? string.Empty
                : (sum / count).ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Сравнение строк по одному столбцу с учётом приоритетов для скиллов.</summary>
    private sealed class RowComparer(TableColumn column, IEnumerable<SkillSortOption> priority)
        : IComparer<TableRowViewModel>
    {
        private readonly SkillSortKey[] _priority = [.. priority.Select(option => option.Key)];

        public int Compare(TableRowViewModel? x, TableRowViewModel? y)
        {
            TableCell? left = x?.CellOf(column);
            TableCell? right = y?.CellOf(column);

            if (left is null || right is null)
            {
                return 0;
            }

            return column.Kind switch
            {
                TableCellKind.Options => IndexOfOption(left).CompareTo(IndexOfOption(right)),
                TableCellKind.Integer => CompareSkills(left, right),
                _ => string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase),
            };
        }

        /// <summary>Редкость сравнивается порядком вариантов: 0 младше 00, то — 000.</summary>
        private int IndexOfOption(TableCell cell)
        {
            for (int i = 0; i < column.Options.Count; i++)
            {
                if (column.Options[i] == cell.Value)
                {
                    return i;
                }
            }

            return -1;
        }

        private int CompareSkills(TableCell left, TableCell right)
        {
            foreach (SkillSortKey key in _priority)
            {
                int result = key switch
                {
                    SkillSortKey.Damage => (left.Number ?? 0.0).CompareTo(right.Number ?? 0.0),
                    SkillSortKey.Type => IndexIn(ElementOptions.DamageTypes, left.SkillType)
                        .CompareTo(IndexIn(ElementOptions.DamageTypes, right.SkillType)),
                    _ => IndexIn(ElementOptions.Sins, left.SkillSin)
                        .CompareTo(IndexIn(ElementOptions.Sins, right.SkillSin)),
                };

                if (result != 0)
                {
                    return result;
                }
            }

            return 0;
        }

        /// <summary>Незаданный тип или грех идёт перед всеми заданными.</summary>
        private static int IndexIn(IReadOnlyList<ElementOption> options, ElementOption? option)
        {
            if (option is null)
            {
                return -1;
            }

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Element == option.Element)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>Все двенадцать грешников в порядке номеров — в нём же они сортируются.</summary>
    public static IReadOnlyList<string> Sinners { get; } =
    [
        "Yi Sang", "Faust", "Don Quixote", "Ryoshu", "Meursault", "Hong Lu",
        "Heathcliff", "Ishmael", "Rodion", "Sinclair", "Outis", "Gregor",
    ];

    /// <summary>Столбцы таблицы личностей. Свободное место забирает Name.</summary>
    public static TableViewModel CreateIdTable() => Create("ID",
    [
        new TableColumn
        {
            Title = "Rarity",
            Width = 80,
            Kind = TableCellKind.Options,
            Options = ["0", "00", "000"],
        },
        new TableColumn
        {
            Title = "Sinner",
            Width = 140,
            Kind = TableCellKind.Options,
            Options = Sinners,
        },
        new TableColumn
        {
            Title = "ID Name",
            Width = 240,
            Stretch = true,
            Aliases = ["Name"],
        },
        .. NumberColumns("S1-1", "S1-2", "S2-1", "S2-2", "S3-1", "S3-2", "S3-3", "S3-4", "C-1", "C-2"),
    ]);

    /// <summary>Столбцы таблицы E.G.O.</summary>
    public static TableViewModel CreateEgoTable() => Create("E.G.O.",
    [
        .. NumberColumns(130, "Danger Level"),
        new TableColumn { Title = "Name", Width = 280, Stretch = true },
        .. NumberColumns(130, "Awakening", "Corrosion"),
    ]);

    private static TableViewModel Create(string title, IReadOnlyList<TableColumn> columns)
    {
        TableColumn.MeasureStretch(columns);

        // Варианты редкости берём у самого столбца: фильтр не должен знать их отдельно.
        IReadOnlyList<string> rarities =
            columns.FirstOrDefault(column => column.Title == "Rarity")?.Options ?? [];

        TableViewModel table = new()
        {
            Title = title,
            Columns = columns,
            Filter = new TableFilterViewModel(Sinners, rarities),
        };

        table.Filter.Changed += (_, _) => table.ApplyFilter();
        return table;
    }

    /// <summary>Целочисленные столбцы одной ширины — их в таблицах большинство.</summary>
    private static IEnumerable<TableColumn> NumberColumns(params string[] titles) =>
        NumberColumns(92, titles);

    private static IEnumerable<TableColumn> NumberColumns(double width, params string[] titles) =>
        titles.Select(title => new TableColumn
        {
            Title = title,
            Width = width,
            Kind = TableCellKind.Integer,
        });
}
