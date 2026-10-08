using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace LimbusCalc.ViewModels;

/// <summary>What a column's cells hold.</summary>
public enum TableCellKind
{
    /// <summary>Free text.</summary>
    Text,

    /// <summary>Whole numbers only.</summary>
    Integer,

    /// <summary>A choice from a fixed list of values.</summary>
    Options,

    /// <summary>Calculated from other cells of the row; not editable.</summary>
    Computed,
}

/// <summary>Where a cell's damage came from.</summary>
public enum TableCellSource
{
    /// <summary>Empty: no damage.</summary>
    Empty,

    /// <summary>Typed in by hand.</summary>
    Manual,

    /// <summary>Exported from the calculator; the setup is stored with it.</summary>
    Calculator,
}

/// <summary>State of the table's file, shown next to the file buttons.</summary>
public enum TableSaveState
{
    /// <summary>The file matches what's on screen.</summary>
    Saved,

    /// <summary>An edit is waiting to be written or is being written right now.</summary>
    Saving,

    /// <summary>Writing failed; it is retried on the next edit and on exit.</summary>
    Failed,

    /// <summary>The file couldn't be read or set aside, so it must not be overwritten.</summary>
    Off,
}

/// <summary>What skill cells are compared by when sorting.</summary>
public enum SkillSortKey
{
    Damage,
    Type,
    Sin,
}

/// <summary>An item of the sort priority list: the key and its label.</summary>
public sealed class SkillSortOption
{
    public required SkillSortKey Key { get; init; }

    public required string Name { get; init; }
}

/// <summary>A reference table column: header title, width and cell kind.</summary>
public sealed class TableColumn : ObservableObject
{
    private string _indicator = string.Empty;
    private double? _actualWidth;
    private bool _isHidden;
    private double[] _scale = [];

    private string? _key;

    public required string Title { get; init; }

    /// <summary>
    /// The name the column is stored under in the file and looked up by. Usually the same as
    /// the title, but the four DPSC columns share one title and need different keys.
    /// </summary>
    public string Key
    {
        get => _key ?? Title;
        init => _key = value;
    }

    /// <summary>Column width; for the stretching column, the minimum width.</summary>
    public required double Width { get; init; }

    public TableCellKind Kind { get; init; } = TableCellKind.Text;

    /// <summary>
    /// Whether the column accepts exports from the calculator. It also means the cell holds
    /// damage: only such cells get a type and a sin.
    /// </summary>
    public bool AcceptsSetup { get; init; }

    /// <summary>What is divided by what, for <see cref="TableCellKind.Computed"/>.</summary>
    public string? DividendKey { get; init; }

    public string? DivisorKey { get; init; }

    /// <summary>Choices for <see cref="TableCellKind.Options"/>; empty for other kinds.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>
    /// Former names of the column, used when reading files: the column may have been renamed
    /// after an export was saved, and that's no reason to lose data.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Whether the column answers to this name — its own or a former one.</summary>
    public bool Matches(string title) =>
        Key == title || Title == title || Aliases.Contains(title);

    /// <summary>How the column is labeled in dropdowns.</summary>
    public override string ToString() => Title;

    /// <summary>
    /// Font size of the column's cells; when null, the table's size is used. Smaller where
    /// long names are cramped: E.G.O. names often include the original in brackets.
    /// </summary>
    public double? FontSize { get; init; }

    /// <summary>The column takes all width the others leave.</summary>
    public bool Stretch { get; init; }

    /// <summary>
    /// How much the other columns take. Calculated when the table is built and when columns
    /// are hidden; that's enough for the stretching column to size itself to the window.
    /// </summary>
    public double OtherWidth { get; private set; }

    /// <summary>Sort direction arrow on the column the table is sorted by.</summary>
    public string Indicator
    {
        get => _indicator;
        internal set => SetProperty(ref _indicator, value);
    }

    /// <summary>
    /// The width the column is drawn with right now; for the stretching column it depends on
    /// the window. Cells take it from here, so the header, rows and averages always agree
    /// no matter where the viewport width was measured.
    /// </summary>
    public double ActualWidth
    {
        get => _isHidden ? 0.0 : _actualWidth ?? Width;
        internal set
        {
            if (_actualWidth != value)
            {
                _actualWidth = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>A description shown over the column header; null means nothing to explain.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether the column is hidden. Its data stays and is still saved — it just isn't shown.
    /// A hidden column has zero width, so the header, rows and averages shift together.
    /// </summary>
    public bool IsHidden
    {
        get => _isHidden;
        internal set
        {
            if (SetProperty(ref _isHidden, value))
            {
                OnPropertyChanged(nameof(ActualWidth));
            }
        }
    }

    /// <summary>The name column can't be hidden: rows would be impossible to tell apart.</summary>
    public bool CanHide => !Matches("Name");

    /// <summary>
    /// Values of the visible cells in ascending order; the damage color scale is based on them.
    /// Empty for columns without a scale or without numbers.
    /// </summary>
    public IReadOnlyList<double> Scale => _scale;

    /// <summary>
    /// Where a value sits on the column's scale: 0 is the lowest, 1 the highest. Based on rank
    /// among the others rather than magnitude: a single outlier like 606 among typical 20–100
    /// would otherwise squash the whole scale to zero and hide the differences.
    /// </summary>
    public double? ScaleOf(double value)
    {
        if (_scale.Length == 0)
        {
            return null;
        }

        // A single value in the column is also the best one.
        if (_scale.Length == 1)
        {
            return 1.0;
        }

        // Equal values share one rank: the middle of their run.
        int first = LowerBound(value);
        int last = LowerBound(Math.BitIncrement(value)) - 1;
        double rank = last < first ? first : (first + last) / 2.0;

        return Math.Clamp(rank / (_scale.Length - 1), 0.0, 1.0);
    }

    private int LowerBound(double value)
    {
        int low = 0;
        int high = _scale.Length;

        while (low < high)
        {
            int middle = (low + high) / 2;

            if (_scale[middle] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>Whether the column is on the color scale: damage and its cost per sin.</summary>
    public bool HasScale => AcceptsSetup || Kind == TableCellKind.Computed;

    internal void SetScale(double[] sorted)
    {
        if (_scale.AsSpan().SequenceEqual(sorted))
        {
            return;
        }

        _scale = sorted;
        OnPropertyChanged(nameof(Scale));
    }

    internal static void MeasureStretch(IReadOnlyList<TableColumn> columns)
    {
        foreach (TableColumn column in columns)
        {
            if (column.Stretch)
            {
                column.OtherWidth = columns
                    .Where(other => other != column && !other.IsHidden)
                    .Sum(other => other.Width);
            }
        }
    }
}

/// <summary>
/// A table cell. The value is stored as text: an empty cell means "no data", not zero,
/// and such cells sort to the bottom. A skill cell can have a damage type and a sin,
/// which are sortable too.
/// </summary>
public sealed class TableCell : ObservableObject
{
    private string _value = string.Empty;
    private string? _setup;
    private ElementOption? _skillType;
    private ElementOption? _skillSin;
    private bool _isVisible = true;

    /// <summary>The column the cell belongs to; width and kind come from it.</summary>
    public required TableColumn Column { get; init; }

    /// <summary>
    /// The row the cell is in. Computed columns need it to read their neighbors, and searching
    /// for the row on every edit would be expensive.
    /// </summary>
    internal TableRowViewModel? Row { get; set; }

    /// <summary>
    /// The cell's content is about to change. Raised before the edit while the old content
    /// can still be captured: the table builds its undo history from it.
    /// </summary>
    internal event EventHandler? Changing;

    public string Value
    {
        get => _value;
        set
        {
            if (Change(ref _value, value))
            {
                OnPropertyChanged(nameof(Source));
            }
        }
    }

    /// <summary>
    /// The calculator setup the value came from, as stored in the file.
    /// Null means the value was typed by hand and there's nothing to send back to the calculator.
    /// </summary>
    public string? Setup
    {
        get => _setup;
        set
        {
            if (Change(ref _setup, value))
            {
                OnPropertyChanged(nameof(HasSetup));
                OnPropertyChanged(nameof(CanEditMarks));
                OnPropertyChanged(nameof(Source));
            }
        }
    }

    /// <summary>Whether there's a setup to send back to the calculator.</summary>
    public bool HasSetup => !string.IsNullOrEmpty(Setup);

    /// <summary>
    /// Whether type and sin can be set by hand. Only damage cells have them, and a cell with a
    /// setup gets them from the calculator, so there's nothing to edit separately.
    /// </summary>
    public bool CanEditMarks => Column.AcceptsSetup && !HasSetup;

    /// <summary>
    /// Where the damage came from. A cell with a setup isn't edited by hand: otherwise the
    /// number and the setup would disagree, and it'd be unclear what goes back to the calculator.
    /// </summary>
    public TableCellSource Source =>
        IsEmpty ? TableCellSource.Empty
        : HasSetup ? TableCellSource.Calculator
        : TableCellSource.Manual;

    /// <summary>Damage type of the skill; when null the cell is just a number.</summary>
    public ElementOption? SkillType
    {
        get => _skillType;
        set => Change(ref _skillType, value);
    }

    /// <summary>Sin of the skill.</summary>
    public ElementOption? SkillSin
    {
        get => _skillSin;
        set => Change(ref _skillSin, value);
    }

    /// <summary>Everything entered in the cell — an edit can be undone from this snapshot.</summary>
    internal TableCellState State => new(Value, Setup, SkillType, SkillSin);

    /// <summary>Puts previously captured content back into the cell.</summary>
    internal void Restore(TableCellState state)
    {
        Value = state.Value;
        SkillType = state.SkillType;
        SkillSin = state.SkillSin;
        Setup = state.Setup;
    }

    /// <summary>Like <see cref="ObservableObject.SetProperty"/>, but raises <see cref="Changing"/> first.</summary>
    private bool Change<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        Changing?.Invoke(this, EventArgs.Empty);
        return SetProperty(ref field, value, propertyName);
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    /// <summary>
    /// Whether the value passes the filter. A cell that doesn't stays blank: its damage
    /// isn't shown or counted in the average.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetProperty(ref _isVisible, value);
    }

    /// <summary>The cell's number, or null if it isn't a number.</summary>
    public double? Number =>
        double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
}

/// <summary>A table row: one cell per column, in the same order.</summary>
public sealed class TableRowViewModel : ObservableObject
{
    private bool _isVisible = true;

    public required IReadOnlyList<TableCell> Cells { get; init; }

    /// <summary>Whether the row passes the filter.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetProperty(ref _isVisible, value);
    }

    /// <summary>The cell of the given column, or null if there is no such column.</summary>
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
    /// The same by column name — this is how rows are read from a file. Former column names
    /// match too, otherwise older exports would lose the column.
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
/// A cell of the averages row under the table. It sits in the same column as the data,
/// so it takes the width from there.
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

/// <summary>An item of the columns list: a "show" checkbox.</summary>
public sealed class ColumnChoiceViewModel : ObservableObject
{
    private readonly TableViewModel _table;

    internal ColumnChoiceViewModel(TableViewModel table, TableColumn column)
    {
        _table = table;
        Column = column;
        column.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TableColumn.IsHidden))
            {
                OnPropertyChanged(nameof(IsShown));
            }
        };
    }

    public TableColumn Column { get; }

    /// <summary>
    /// The label in the list. The four DPSC columns share one header title, so the key
    /// ("1T Damage DPSC") is used here — otherwise they couldn't be told apart.
    /// </summary>
    public string Title => Column.Key;

    public bool IsShown
    {
        get => !Column.IsHidden;
        set => _table.SetColumnHidden(Column, !value);
    }
}

/// <summary>
/// A reference table with a given set of columns. Shared by ID and E.G.O.: they differ only
/// in columns, the behavior is the same.
/// </summary>
public sealed class TableViewModel : ObservableObject
{
    private TableColumn? _sortColumn;
    private bool _sortDescending;
    private IReadOnlyList<TableAverage>? _averages;
    private int _bulkDepth;
    private bool _bulkChanged;
    private bool _isLoading;
    private bool _replaying;
    private TableSaveState _saveState;
    private string _saveProblem = string.Empty;

    /// <summary>
    /// The cell being edited in the editor. All its edits until the editor closes are one
    /// history step: otherwise every typed digit would be undone separately.
    /// </summary>
    private TableCell? _editingCell;

    private CellStep? _editingStep;

    private string _filterSummary = string.Empty;
    private IReadOnlyList<ColumnChoiceViewModel>? _columnChoices;

    /// <summary>Viewport width from the last layout; the Name column stretches to it.</summary>
    private double _viewportWidth;

    private string _editingLabel = string.Empty;

    /// <summary>The title above the table.</summary>
    public required string Title { get; init; }

    public required IReadOnlyList<TableColumn> Columns { get; init; }

    /// <summary>Row and value filter; created together with the table.</summary>
    public required TableFilterViewModel Filter { get; init; }

    /// <summary>Whether there's a Sinner column — decides whether the sinner filter is shown.</summary>
    public bool HasSinners => Columns.Any(column => column.Title == "Sinner");

    /// <summary>Whether there's a Rarity column — decides whether its filter is shown.</summary>
    public bool HasRarity => Columns.Any(column => column.Title == "Rarity");

    /// <summary>Whether there's an E.G.O. Type column — decides whether its filter is shown.</summary>
    public bool HasEgoType => Columns.Any(column => column.Title == "Type");

    /// <summary>How many rows are left after filtering: "12 of 210 rows".</summary>
    public string FilterSummary
    {
        get => _filterSummary;
        private set => SetProperty(ref _filterSummary, value);
    }

    /// <summary>Columns with "show" checkboxes; the name column isn't listed.</summary>
    public IReadOnlyList<ColumnChoiceViewModel> ColumnChoices =>
        _columnChoices ??= [.. Columns.Where(column => column.CanHide).Select(column => new ColumnChoiceViewModel(this, column))];

    /// <summary>Label of the columns button: how many are hidden.</summary>
    public string ColumnsLabel
    {
        get
        {
            int hidden = Columns.Count(column => column.IsHidden);

            return hidden == 0 ? "Columns" : $"Columns: {hidden} hidden";
        }
    }

    /// <summary>The sort column and direction — to remember between launches.</summary>
    public string? SortKey => _sortColumn?.Key;

    public bool SortDescending => _sortDescending;

    /// <summary>
    /// Hides or shows a column. The stretching column takes the freed space, so widths are
    /// recalculated right away for the same viewport.
    /// </summary>
    public void SetColumnHidden(TableColumn column, bool hidden)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (!column.CanHide || column.IsHidden == hidden)
        {
            return;
        }

        column.IsHidden = hidden;
        TableColumn.MeasureStretch(Columns);
        UpdateColumnWidths(_viewportWidth);
        OnPropertyChanged(nameof(ColumnsLabel));
    }

    public void ShowAllColumns()
    {
        foreach (TableColumn column in Columns)
        {
            SetColumnHidden(column, false);
        }
    }

    /// <summary>Hides everything that can be hidden, leaving only the name to pick columns from.</summary>
    public void HideAllColumns()
    {
        foreach (TableColumn column in Columns)
        {
            SetColumnHidden(column, true);
        }
    }

    /// <summary>
    /// Restores the sort remembered from the last launch. The column with that key may no
    /// longer exist — then the table stays unsorted.
    /// </summary>
    public void RestoreSort(string? columnKey, bool descending, IReadOnlyList<SkillSortKey> priority)
    {
        ArgumentNullException.ThrowIfNull(priority);

        for (int target = 0; target < priority.Count; target++)
        {
            int index = IndexOfPriority(priority[target]);

            if (index >= 0 && target < SortPriority.Count && index != target)
            {
                SortPriority.Move(index, target);
            }
        }

        _sortColumn = Columns.FirstOrDefault(column => column.Key == columnKey);
        _sortDescending = _sortColumn is not null && descending;
        ApplySort();
    }

    private int IndexOfPriority(SkillSortKey key)
    {
        for (int i = 0; i < SortPriority.Count; i++)
        {
            if (SortPriority[i].Key == key)
            {
                return i;
            }
        }

        return -1;
    }

    public ObservableCollection<TableRowViewModel> Rows { get; } = [];

    /// <summary>
    /// What skill cells are compared by, from the main key to the last.
    /// Damage, then type, then sin by default; the order is edited from the column menu.
    /// </summary>
    public ObservableCollection<SkillSortOption> SortPriority { get; } =
    [
        new SkillSortOption { Key = SkillSortKey.Damage, Name = "Damage" },
        new SkillSortOption { Key = SkillSortKey.Type, Name = "Skill type" },
        new SkillSortOption { Key = SkillSortKey.Sin, Name = "Skill sin" },
    ];

    /// <summary>
    /// The row under the table: average damage per skill column. Created once per column and
    /// only recalculated afterwards, so bindings don't break.
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

    /// <summary>Whether there's anything to delete; disables the Clear button when false.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>The table is empty — a hint is shown instead of rows.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// The table is still being read from its file. Until then a cover hides it: an edit
    /// made before reading finishes would be overwritten by what was read.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>What can be undone and redone.</summary>
    public TableHistory History { get; } = new();

    /// <summary>Whether the table's file is written — for the indicator next to the file buttons.</summary>
    public TableSaveState SaveState
    {
        get => _saveState;
        set => SetProperty(ref _saveState, value);
    }

    /// <summary>Why it isn't written; shown when the indicator is clicked.</summary>
    public string SaveProblem
    {
        get => _saveProblem;
        set => SetProperty(ref _saveProblem, value);
    }

    /// <summary>
    /// The content changed: a row was added or removed, a cell edited, the rows re-sorted.
    /// This event sends the table to disk.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Whether edits go into the history. Bulk changes are how tables load from a file —
    /// there's nothing to undo there, and <see cref="Record"/> records a whole load as one step.
    /// Undo itself edits cells too, and must not be recorded as a new edit.
    /// </summary>
    private bool IsRecording => _bulkDepth == 0 && !_replaying;

    /// <summary>Appends an empty row. Recorded in the history unless the table is loading.</summary>
    public TableRowViewModel AddRow()
    {
        TableRowViewModel row = NewRow();

        if (IsRecording)
        {
            History.Push(new RowStep { Row = row, Index = Rows.Count, Insert = false, Label = "Add row" });
        }

        InsertRow(Rows.Count, row);
        return row;
    }

    /// <summary>
    /// The copy goes right below the row, with all numbers, marks and setups. "(copy)" is
    /// appended to the name — otherwise the two rows couldn't be told apart, neither in the
    /// table nor when exporting from the calculator.
    /// </summary>
    public TableRowViewModel Duplicate(TableRowViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int index = Rows.IndexOf(source);

        if (index < 0)
        {
            throw new ArgumentException("The row is not in this table.", nameof(source));
        }

        TableRowViewModel copy = NewRow();

        // The table isn't listening to the copy's cells yet, so this isn't recorded.
        for (int i = 0; i < copy.Cells.Count; i++)
        {
            if (copy.Cells[i].Column.Kind != TableCellKind.Computed)
            {
                copy.Cells[i].Restore(source.Cells[i].State);
            }
        }

        if (copy.CellOf("Name") is TableCell name && !name.IsEmpty)
        {
            name.Value = $"{name.Value.Trim()} (copy)";
        }

        Recompute(copy);

        if (IsRecording)
        {
            History.Push(new RowStep { Row = copy, Index = index + 1, Insert = false, Label = $"Duplicate {NameOf(source)}" });
        }

        InsertRow(index + 1, copy);
        return copy;
    }

    private TableRowViewModel NewRow()
    {
        TableRowViewModel row = new()
        {
            Cells = [.. Columns.Select(column => new TableCell { Column = column })],
        };

        foreach (TableCell cell in row.Cells)
        {
            cell.Row = row;
        }

        return row;
    }

    /// <summary>Removes a whole row together with the setups stored in its cells.</summary>
    public void Remove(TableRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        int index = Rows.IndexOf(row);

        if (index < 0)
        {
            return;
        }

        if (IsRecording)
        {
            History.Push(new RowStep { Row = row, Index = index, Insert = true, Label = $"Delete {NameOf(row)}" });
        }

        DetachRow(row);
    }

    /// <summary>Removes every row as a single undoable step.</summary>
    public void Clear() =>
        Record("Clear table", () =>
        {
            while (Rows.Count > 0)
            {
                DetachRow(Rows[^1]);
            }
        });

    /// <summary>
    /// Changes the table's rows as one history step — used for loading files and clearing.
    /// If it fails midway the previous rows come back: half a table is worse than none.
    /// </summary>
    public void Record(string label, Action change)
    {
        ArgumentNullException.ThrowIfNull(change);

        bool recording = IsRecording;
        List<TableRowViewModel> before = [.. Rows];

        try
        {
            using IDisposable bulk = BeginBulkChange();

            change();
        }
        catch
        {
            ReplaceRows(before);
            throw;
        }

        if (recording && !before.SequenceEqual(Rows))
        {
            History.Push(new RowsStep { Rows = before, Label = label });
        }
    }

    /// <summary>Undoes the last edit; does nothing if there's nothing to undo.</summary>
    public bool Undo()
    {
        // An open cell edit is a step too. Close it before taking a step from the history.
        EndCellEdit(null);
        return Replay(History.TakeUndo(), History.PutRedo);
    }

    /// <summary>Redoes the last undone edit.</summary>
    public bool Redo()
    {
        EndCellEdit(null);
        return Replay(History.TakeRedo(), History.PutUndo);
    }

    private bool Replay(TableStep? step, Action<TableStep> keepInverse)
    {
        if (step is null)
        {
            return false;
        }

        TableStep? inverse = null;
        Replaying(() => inverse = step.Revert(this));
        keepInverse(inverse!);
        return true;
    }

    /// <summary>Edits the table bypassing the history, as one bulk change.</summary>
    private void Replaying(Action change)
    {
        _replaying = true;

        try
        {
            using IDisposable bulk = BeginBulkChange();

            change();
        }
        finally
        {
            _replaying = false;
        }
    }

    /// <summary>
    /// Opens a cell edit: everything that happens to the cell until it closes is undone as one
    /// step, and <see cref="CellEdit.Cancel"/> puts it back as it was. Also used for edits
    /// from code that change several properties of a cell at once.
    /// </summary>
    public CellEdit BeginCellEdit(TableCell cell, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(cell);

        EndCellEdit(null);

        _editingCell = cell;
        _editingStep = null;
        _editingLabel = label ?? EditLabel(cell);

        return new CellEdit(this, cell);
    }

    /// <summary>Closes the edit. If the value is back to what it was, no step is needed.</summary>
    internal void EndCellEdit(TableCell? cell)
    {
        if (_editingCell is null || (cell is not null && !ReferenceEquals(cell, _editingCell)))
        {
            return;
        }

        if (_editingStep is not null)
        {
            if (_editingStep.Before == _editingCell.State)
            {
                History.Drop(_editingStep);
            }
            else
            {
                History.ForgetRedo();
            }
        }

        _editingCell = null;
        _editingStep = null;
    }

    internal void CancelCellEdit(TableCell cell)
    {
        if (!ReferenceEquals(cell, _editingCell))
        {
            return;
        }

        if (_editingStep is CellStep step)
        {
            Replaying(() => cell.Restore(step.Before));
        }

        EndCellEdit(cell);
    }

    private void OnCellChanging(object? sender, EventArgs e)
    {
        // Computed cells recalculate themselves; there's no point undoing them separately.
        if (sender is not TableCell cell || cell.Column.Kind == TableCellKind.Computed || !IsRecording)
        {
            return;
        }

        if (ReferenceEquals(cell, _editingCell))
        {
            if (_editingStep is null)
            {
                _editingStep = new CellStep { Cell = cell, Before = cell.State, Label = _editingLabel };
                History.Push(_editingStep, keepRedo: true);
            }

            return;
        }

        History.Push(new CellStep { Cell = cell, Before = cell.State, Label = EditLabel(cell) });
    }

    private static string EditLabel(TableCell cell)
    {
        string name = cell.Row is null ? string.Empty : NameOf(cell.Row);

        return cell.Column.Matches("Name") || cell.Row is null || name == "row"
            ? $"Edit {cell.Column.Title}"
            : $"Edit {cell.Column.Title} of {name}";
    }

    /// <summary>How to name a row in a tooltip: by its name, or just "row" without one.</summary>
    private static string NameOf(TableRowViewModel row)
    {
        string name = row.CellOf("Name")?.Value.Trim() ?? string.Empty;

        return name.Length == 0 ? "row" : $"“{name}”";
    }

    /// <summary>Puts a row into the table and starts listening to its cells.</summary>
    internal void InsertRow(int index, TableRowViewModel row)
    {
        foreach (TableCell cell in row.Cells)
        {
            cell.PropertyChanged += OnCellChanged;
            cell.Changing += OnCellChanging;
        }

        Rows.Insert(index, row);
        OnRowsChanged();
    }

    /// <summary>Removes a row; its cells no longer report to the table.</summary>
    internal void DetachRow(TableRowViewModel row)
    {
        if (!Rows.Remove(row))
        {
            return;
        }

        foreach (TableCell cell in row.Cells)
        {
            cell.PropertyChanged -= OnCellChanged;
            cell.Changing -= OnCellChanging;
        }

        OnRowsChanged();
    }

    /// <summary>Replaces all rows of the table with the given ones.</summary>
    internal void ReplaceRows(IReadOnlyList<TableRowViewModel> rows)
    {
        using IDisposable bulk = BeginBulkChange();

        while (Rows.Count > 0)
        {
            DetachRow(Rows[^1]);
        }

        foreach (TableRowViewModel row in rows)
        {
            InsertRow(Rows.Count, row);
        }
    }

    /// <summary>
    /// Sorts by this column; clicking it again reverses the order.
    /// Cells without data go to the bottom either way: there's nothing to compare.
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

    /// <summary>Moves a key in the priority list: -1 up, +1 down.</summary>
    public void MovePriority(SkillSortOption option, int delta)
    {
        int index = SortPriority.IndexOf(option);
        int target = index + delta;

        if (index < 0 || target < 0 || target >= SortPriority.Count)
        {
            return;
        }

        SortPriority.Move(index, target);

        // The key order changed, so the table must be re-sorted right away.
        if (_sortColumn?.Kind == TableCellKind.Integer)
        {
            ApplySort();
        }
    }

    /// <summary>
    /// Recalculates column widths for the viewport: the stretching column takes the rest.
    /// Called on resize — the header, rows and averages take their widths from here,
    /// so they stay aligned.
    /// </summary>
    public void UpdateColumnWidths(double viewportWidth)
    {
        _viewportWidth = viewportWidth;

        foreach (TableColumn column in Columns)
        {
            // One pixel goes to the table's outer border; without it the row overflows
            // and an unnecessary horizontal scrollbar appears.
            column.ActualWidth = column.Stretch && viewportWidth > 0.0
                ? Math.Max(column.Width, viewportWidth - column.OtherWidth - 1.0)
                : column.Width;
        }
    }

    /// <summary>Re-sorts by the current column; does nothing when no column is chosen.</summary>
    public void ApplySort()
    {
        UpdateIndicators();

        if (_sortColumn is null || Rows.Count < 2)
        {
            return;
        }

        TableColumn column = _sortColumn;
        RowComparer comparer = new(column, SortPriority);

        // Empty cells aren't the smallest value but missing data: they stay at the bottom
        // in both directions.
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
    /// Starts a bulk change: filters and averages are recalculated once at the end instead
    /// of after every cell. When loading a table that's tens of times faster — otherwise
    /// every one of thousands of edits walks the whole table again.
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

        // Edits didn't react during the bulk change, so everything is recalculated here.
        RecomputeAll();

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnCellChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Visibility is set by the filter itself. It's not a data edit: neither re-filtering
        // nor saving is needed — and on a thousand cells such a second pass cost seconds.
        if (e.PropertyName == nameof(TableCell.IsVisible))
        {
            return;
        }

        // Editing a regular cell may change what is calculated from it. Computed cells don't
        // trigger a recalculation themselves, so the chain stops there.
        if (sender is TableCell { Row: TableRowViewModel row } changed
            && changed.Column.Kind != TableCellKind.Computed)
        {
            Recompute(row);
        }

        if (_bulkDepth > 0)
        {
            _bulkChanged = true;
            return;
        }

        // Editing a mark or a value may move the row in or out of the filter.
        ApplyFilter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Recalculates the row's computed cells. With nothing to divide or nothing to divide by
    /// the cell stays empty: zero would mean "calculated and came out as zero".
    /// </summary>
    private static void Recompute(TableRowViewModel row)
    {
        foreach (TableCell cell in row.Cells)
        {
            if (cell.Column.Kind != TableCellKind.Computed)
            {
                continue;
            }

            double? dividend = Value(row, cell.Column.DividendKey);
            double? divisor = Value(row, cell.Column.DivisorKey);

            cell.Value = dividend is double top && divisor is double bottom && bottom != 0.0
                ? (top / bottom).ToString("0.##", CultureInfo.InvariantCulture)
                : string.Empty;
        }
    }

    private static double? Value(TableRowViewModel row, string? columnKey) =>
        columnKey is null ? null : row.CellOf(columnKey)?.Number;

    /// <summary>Recalculates computed cells in the whole table — after reading a file.</summary>
    private void RecomputeAll()
    {
        foreach (TableRowViewModel row in Rows)
        {
            Recompute(row);
        }
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
    /// Runs the rows through the filter: sinner selects whole rows, type and sin select
    /// individual values. A row with no matching value left is hidden entirely.
    /// </summary>
    public void ApplyFilter()
    {
        foreach (TableRowViewModel row in Rows)
        {
            // Sinner, rarity, E.G.O. type and search select whole rows; damage type and sin
            // select its values. The name column is named differently in ID and E.G.O.,
            // but both answer to "Name".
            bool rowOk = Filter.AllowsName(row.CellOf("Name")?.Value)
                && Filter.AllowsSinner(row.CellOf("Sinner")?.Value)
                && Filter.AllowsRarity(row.CellOf("Rarity")?.Value)
                && Filter.AllowsEgoType(row.CellOf("Type")?.Value);

            bool anyValue = false;

            foreach (TableCell cell in row.Cells)
            {
                // Only damage is filtered by type and sin. Sin Cost and other numbers without
                // marks stay visible, otherwise the filter would hide them along with damage.
                if (!cell.Column.AcceptsSetup)
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
        UpdateScales();

        FilterSummary = Filter.IsActive
            ? $"{Rows.Count(row => row.IsVisible)} of {Rows.Count} rows"
            : string.Empty;
    }

    /// <summary>
    /// The color scale per column: everything visible, in ascending order. Values hidden by the
    /// filter don't affect the scale — they aren't visible either.
    /// </summary>
    private void UpdateScales()
    {
        foreach (TableColumn column in Columns)
        {
            if (!column.HasScale)
            {
                continue;
            }

            List<double> values = [];

            foreach (TableRowViewModel row in Rows)
            {
                if (row.IsVisible && row.CellOf(column) is { IsVisible: true, Number: double value })
                {
                    values.Add(value);
                }
            }

            values.Sort();
            column.SetScale([.. values]);
        }
    }

    /// <summary>
    /// Average damage per skill column. Empty cells don't count: an unfilled skill isn't zero
    /// damage, and there's no reason to drag the average down with it.
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

            if (average.Column.Kind is not (TableCellKind.Integer or TableCellKind.Computed))
            {
                average.Text = string.Empty;
                continue;
            }

            double sum = 0.0;
            int count = 0;

            foreach (TableRowViewModel row in Rows)
            {
                // The average covers what's visible: values hidden by the filter don't count.
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

    /// <summary>Compares rows by one column, using the priorities for skill columns.</summary>
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
                // Computed columns have no marks — there's nothing to compare but the number.
                TableCellKind.Computed => (left.Number ?? 0.0).CompareTo(right.Number ?? 0.0),
                _ => string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase),
            };
        }

        /// <summary>Rarity compares by option order: 0 is below 00, which is below 000.</summary>
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

        /// <summary>An unset type or sin comes before all set ones.</summary>
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

    /// <summary>All twelve sinners in number order — they sort in this order too.</summary>
    public static IReadOnlyList<string> Sinners { get; } =
    [
        "Yi Sang", "Faust", "Don Quixote", "Ryoshu", "Meursault", "Hong Lu",
        "Heathcliff", "Ishmael", "Rodion", "Sinclair", "Outis", "Gregor",
    ];

    /// <summary>Columns of the ID table. Name takes the free space.</summary>
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
        .. SkillColumns("S1-1", "S1-2", "S2-1", "S2-2", "S3-1", "S3-2", "S3-3", "S3-4", "C-1", "C-2"),
    ]);

    /// <summary>Columns of the E.G.O. table. Every damage column is followed by its DPSC.</summary>
    public static TableViewModel CreateEgoTable() => Create("E.G.O.",
    [
        new TableColumn
        {
            Title = "Danger Level",
            Width = 116,
            Kind = TableCellKind.Options,
            // Ordered from lowest to highest: the column sorts by this order.
            Options = ["ZAYIN", "TETH", "HE", "WAW", "ALEPH"],
        },
        new TableColumn
        {
            Title = "Sinner",
            Width = 118,
            Kind = TableCellKind.Options,
            Options = Sinners,
        },
        new TableColumn
        {
            Title = "Type",
            Width = 106,
            Kind = TableCellKind.Options,
            Options = ["Awakening", "Corrosion"],
        },
        // The width taken from the three narrow columns on the left went to the name.
        new TableColumn { Title = "Name", Width = 270, Stretch = true, FontSize = 12 },
        new TableColumn { Title = "Sin Cost", Width = 92, Kind = TableCellKind.Integer },
        .. DamageWithCost("1T Damage", "Single target"),
        .. DamageWithCost("3T Damage", "Single enemy with 3 parts"),
        .. DamageWithCost("7T Damage", "7 different enemies"),
        .. DamageWithCost(
            "Max Damage",
            "Highest potential damage (could be impossible to achieve in normal gameplay)",
            aliases: ["Max T Damage"]),
    ]);

    /// <summary>
    /// Damage and its cost per sin: DPSC is calculated and can't be edited. All four share
    /// one header title but must be stored separately — hence a separate key.
    /// </summary>
    private static IEnumerable<TableColumn> DamageWithCost(
        string title,
        string description,
        IReadOnlyList<string>? aliases = null)
    {
        yield return new TableColumn
        {
            Title = title,
            Description = description,
            // The former name is kept so older files read without loss.
            Aliases = aliases ?? [],
            Width = 110,
            Kind = TableCellKind.Integer,
            AcceptsSetup = true,
        };

        yield return new TableColumn
        {
            Key = $"{title} DPSC",
            Title = "DPSC",
            Description = "Damage per Sin Cost",
            Width = 76,
            Kind = TableCellKind.Computed,
            DividendKey = title,
            DivisorKey = "Sin Cost",
        };
    }

    private static TableViewModel Create(string title, IReadOnlyList<TableColumn> columns)
    {
        TableColumn.MeasureStretch(columns);

        // Rarity options come from the column itself: the filter shouldn't know them separately.
        IReadOnlyList<string> rarities =
            columns.FirstOrDefault(column => column.Title == "Rarity")?.Options ?? [];

        IReadOnlyList<string> egoTypes =
            columns.FirstOrDefault(column => column.Title == "Type")?.Options ?? [];

        TableViewModel table = new()
        {
            Title = title,
            Columns = columns,
            Filter = new TableFilterViewModel(Sinners, rarities, egoTypes),
        };

        table.Filter.Changed += (_, _) => table.ApplyFilter();
        return table;
    }

    /// <summary>Skill columns: whole numbers that accept calculator exports.</summary>
    private static IEnumerable<TableColumn> SkillColumns(params string[] titles) =>
        titles.Select(title => new TableColumn
        {
            Title = title,
            Width = 92,
            Kind = TableCellKind.Integer,
            AcceptsSetup = true,
        });
}
