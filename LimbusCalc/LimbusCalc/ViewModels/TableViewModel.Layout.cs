using System.Collections.ObjectModel;

namespace LimbusCalc.ViewModels;

/// <summary>Column layout on screen: order and widths chosen by dragging the header.</summary>
public sealed partial class TableViewModel
{
    /// <summary>The narrowest a column can be dragged to.</summary>
    public const double MinColumnWidth = 30;

    /// <summary>
    /// Columns in screen order. The header, rows and averages all follow it, while the data
    /// keeps its own order in <see cref="Columns"/>: moving a column only changes the view.
    /// </summary>
    public ObservableCollection<TableColumn> DisplayColumns { get; } = [];

    /// <summary>Whether the order or any width differs from the default.</summary>
    public bool HasCustomLayout =>
        Columns.Any(column => column.UserWidth is not null)
        || !DisplayColumns.SequenceEqual(Columns);

    /// <summary>Moves a column to another place on screen.</summary>
    public void MoveColumn(TableColumn column, int index)
    {
        ArgumentNullException.ThrowIfNull(column);

        int current = DisplayColumns.IndexOf(column);
        index = Math.Clamp(index, 0, DisplayColumns.Count - 1);

        if (current < 0 || current == index)
        {
            return;
        }

        DisplayColumns.Move(current, index);

        if (_averages is not null)
        {
            _averages.Move(current, index);
        }

        // The "Average" label follows the first text column, which may have changed.
        UpdateAverages();
        OnPropertyChanged(nameof(HasCustomLayout));
    }

    /// <summary>
    /// Sets a column's width. For the stretching column it's the minimum: the column still
    /// takes whatever room the others leave.
    /// </summary>
    public void ResizeColumn(TableColumn column, double width)
    {
        ArgumentNullException.ThrowIfNull(column);

        column.UserWidth = Math.Max(MinColumnWidth, Math.Round(width));
        TableColumn.MeasureStretch(Columns);
        UpdateColumnWidths(_viewportWidth);
        OnPropertyChanged(nameof(HasCustomLayout));
    }

    /// <summary>Puts every column back to its default place and width.</summary>
    public void ResetLayout()
    {
        foreach (TableColumn column in Columns)
        {
            column.UserWidth = null;
        }

        for (int i = 0; i < Columns.Count; i++)
        {
            MoveColumn(Columns[i], i);
        }

        TableColumn.MeasureStretch(Columns);
        UpdateColumnWidths(_viewportWidth);
        OnPropertyChanged(nameof(HasCustomLayout));
    }

    /// <summary>
    /// Restores a remembered layout. Unknown keys are skipped, and columns missing from the
    /// remembered order (added in a newer version) go to the end.
    /// </summary>
    public void RestoreLayout(IReadOnlyList<string> order, IReadOnlyDictionary<string, double> widths)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(widths);

        int target = 0;

        foreach (string key in order)
        {
            if (Columns.FirstOrDefault(column => column.Key == key) is TableColumn column)
            {
                MoveColumn(column, target++);
            }
        }

        foreach (TableColumn column in Columns)
        {
            column.UserWidth = widths.TryGetValue(column.Key, out double width)
                ? Math.Max(MinColumnWidth, width)
                : null;
        }

        TableColumn.MeasureStretch(Columns);
        UpdateColumnWidths(_viewportWidth);
        OnPropertyChanged(nameof(HasCustomLayout));
    }
}
