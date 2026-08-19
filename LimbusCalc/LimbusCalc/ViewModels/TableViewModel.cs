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

    /// <summary>Число клетки или пусто, если там не число.</summary>
    public double? Number =>
        double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
}

/// <summary>Строка таблицы: по клетке на столбец, в том же порядке.</summary>
public sealed class TableRowViewModel
{
    public required IReadOnlyList<TableCell> Cells { get; init; }

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
/// Справочная таблица с заданным набором столбцов. Общая и для ID, и для E.G.O.:
/// отличаются они только столбцами, поведение одно.
/// </summary>
public sealed class TableViewModel : ObservableObject
{
    private TableColumn? _sortColumn;
    private bool _sortDescending;

    /// <summary>Подпись над таблицей.</summary>
    public required string Title { get; init; }

    public required IReadOnlyList<TableColumn> Columns { get; init; }

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

    public void RemoveLastRow()
    {
        if (Rows.Count == 0)
        {
            return;
        }

        foreach (TableCell cell in Rows[^1].Cells)
        {
            cell.PropertyChanged -= OnCellChanged;
        }

        Rows.RemoveAt(Rows.Count - 1);
        OnRowsChanged();
    }

    public void Clear()
    {
        while (Rows.Count > 0)
        {
            RemoveLastRow();
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

    private void OnCellChanged(object? sender, PropertyChangedEventArgs e) =>
        Changed?.Invoke(this, EventArgs.Empty);

    private void OnRowsChanged()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        Changed?.Invoke(this, EventArgs.Empty);
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

        return new TableViewModel { Title = title, Columns = columns };
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
